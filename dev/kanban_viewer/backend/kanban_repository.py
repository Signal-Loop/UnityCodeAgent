from __future__ import annotations

import hashlib
import os
import re
import stat
import tempfile
from dataclasses import dataclass
from pathlib import Path


STATUSES = (
    "Backlog",
    "Started",
    "Planning",
    "Ready",
    "ToDo",
    "InProgress",
    "Completed",
)

_TITLE_PATTERN = re.compile(r"^#\s+(.+?)\s*$", re.MULTILINE)
_STATUS_PATTERN = re.compile(r"^- status:\s*(.*?)\s*$", re.MULTILINE)
_ORDER_PATTERN = re.compile(r"^- order:\s*(.*?)\s*$", re.MULTILINE)
_UTF8_BOM = b"\xef\xbb\xbf"


class KanbanRepositoryError(ValueError):
    """Base error for invalid board operations."""


class InvalidDirectoryError(KanbanRepositoryError):
    """Raised when a task directory cannot be used."""


class InvalidTaskError(KanbanRepositoryError):
    """Raised when a task or move request is invalid."""


class StaleBoardError(KanbanRepositoryError):
    """Raised when a move is based on an outdated board snapshot."""


@dataclass(frozen=True)
class TaskRecord:
    path: str
    title: str
    status: str
    order: int
    version: str


@dataclass(frozen=True)
class ParseWarning:
    path: str
    message: str


@dataclass(frozen=True)
class BoardSnapshot:
    directory: Path
    revision: str
    tasks: tuple[TaskRecord, ...]
    warnings: tuple[ParseWarning, ...]


class KanbanRepository:
    def __init__(self, workspace_root: Path) -> None:
        self.workspace_root = workspace_root.resolve()

    def resolve_directory(self, directory: str | Path) -> Path:
        value = Path(directory).expanduser()
        if not value.is_absolute():
            value = self.workspace_root / value

        try:
            resolved = value.resolve(strict=True)
        except (OSError, RuntimeError) as error:
            raise InvalidDirectoryError(f"Task folder does not exist: {value}") from error

        if not resolved.is_dir():
            raise InvalidDirectoryError(f"Task folder is not a directory: {resolved}")
        return resolved

    def resolve_task_path(self, directory: Path, task_path: str) -> Path:
        relative = Path(task_path.replace("/", os.sep))
        if relative.is_absolute() or ".." in relative.parts or relative.suffix.lower() != ".md":
            raise InvalidTaskError("Task path must be a relative Markdown file path.")

        try:
            resolved = (directory / relative).resolve(strict=True)
        except (OSError, RuntimeError) as error:
            raise InvalidTaskError(f"Task file does not exist: {task_path}") from error

        if not resolved.is_relative_to(directory.resolve()) or not resolved.is_file():
            raise InvalidTaskError("Task path must stay inside the selected task folder.")
        return resolved

    def load_board(self, directory: str | Path) -> BoardSnapshot:
        root = self.resolve_directory(directory)
        tasks: list[TaskRecord] = []
        warnings: list[ParseWarning] = []
        revision_parts: list[str] = []

        paths = sorted(
            (
                path
                for path in root.rglob("*")
                if path.is_file()
                and path.suffix.lower() == ".md"
                and path.name.lower() != "readme.md"
            ),
            key=lambda path: path.relative_to(root).as_posix().casefold(),
        )

        for path in paths:
            relative_path = path.relative_to(root).as_posix()
            try:
                raw = path.read_bytes()
            except OSError as error:
                warnings.append(ParseWarning(relative_path, f"Could not read file: {error}"))
                revision_parts.append(f"{relative_path}:unreadable")
                continue

            version = hashlib.sha256(raw).hexdigest()[:16]
            revision_parts.append(f"{relative_path}:{version}")
            task, task_warning = self._parse_task(relative_path, raw, version)
            if task_warning is not None:
                warnings.append(task_warning)
            elif task is not None:
                tasks.append(task)

        status_index = {status: index for index, status in enumerate(STATUSES)}
        tasks.sort(key=lambda task: (status_index[task.status], task.order, task.path.casefold()))
        warnings.extend(self._duplicate_order_warnings(tasks))

        revision = hashlib.sha256("\n".join(revision_parts).encode("utf-8")).hexdigest()
        return BoardSnapshot(root, revision, tuple(tasks), tuple(warnings))

    def move_task(
        self,
        directory: str | Path,
        task_path: str,
        target_status: str,
        target_index: int,
        revision: str,
    ) -> BoardSnapshot:
        if target_status not in STATUSES:
            raise InvalidTaskError(f"Unknown task status: {target_status}")

        snapshot = self.load_board(directory)
        if snapshot.revision != revision:
            raise StaleBoardError("The board changed on disk. Refresh and try the move again.")

        selected = next((task for task in snapshot.tasks if task.path == task_path), None)
        if selected is None:
            raise InvalidTaskError(f"Task is not part of the current board: {task_path}")

        target_tasks = [
            task
            for task in snapshot.tasks
            if task.status == target_status and task.path != selected.path
        ]
        if target_index < 0 or target_index > len(target_tasks):
            raise InvalidTaskError(
                f"Target index {target_index} is outside the {target_status} column."
            )

        reordered = list(target_tasks)
        reordered.insert(target_index, selected)

        if selected.status == target_status:
            original_paths = [task.path for task in snapshot.tasks if task.status == target_status]
            if original_paths == [task.path for task in reordered]:
                return snapshot

        candidate = self._candidate_order(reordered, target_index)
        root = snapshot.directory
        if candidate is None:
            for index, task in enumerate(reordered, start=1):
                status = target_status if task.path == selected.path else None
                self._update_properties(
                    self.resolve_task_path(root, task.path),
                    status=status,
                    order=index * 100,
                )
        else:
            self._update_properties(
                self.resolve_task_path(root, selected.path),
                status=target_status,
                order=candidate,
            )

        return self.load_board(root)

    @staticmethod
    def _parse_task(
        relative_path: str,
        raw: bytes,
        version: str,
    ) -> tuple[TaskRecord | None, ParseWarning | None]:
        try:
            text = raw.decode("utf-8-sig")
        except UnicodeDecodeError:
            return None, ParseWarning(relative_path, "File is not valid UTF-8.")

        title_matches = _TITLE_PATTERN.findall(text)
        status_matches = _STATUS_PATTERN.findall(text)
        order_matches = _ORDER_PATTERN.findall(text)

        if not title_matches:
            return None, ParseWarning(relative_path, "Missing H1 task title.")
        if len(status_matches) != 1:
            return None, ParseWarning(relative_path, "Expected exactly one '- status:' property.")
        if len(order_matches) != 1:
            return None, ParseWarning(relative_path, "Expected exactly one '- order:' property.")

        status = status_matches[0].strip()
        if status not in STATUSES:
            return None, ParseWarning(relative_path, f"Unknown status: {status or '(empty)'}.")

        try:
            order = int(order_matches[0].strip())
        except ValueError:
            return None, ParseWarning(relative_path, "Order must be a positive integer.")
        if order <= 0:
            return None, ParseWarning(relative_path, "Order must be a positive integer.")

        return (
            TaskRecord(relative_path, title_matches[0].strip(), status, order, version),
            None,
        )

    @staticmethod
    def _duplicate_order_warnings(tasks: list[TaskRecord]) -> list[ParseWarning]:
        groups: dict[tuple[str, int], list[TaskRecord]] = {}
        for task in tasks:
            groups.setdefault((task.status, task.order), []).append(task)

        warnings: list[ParseWarning] = []
        for (status, order), matches in groups.items():
            if len(matches) < 2:
                continue
            paths = ", ".join(task.path for task in matches)
            for task in matches:
                warnings.append(
                    ParseWarning(
                        task.path,
                        f"Duplicate order {order} in {status}: {paths}.",
                    )
                )
        return warnings

    @staticmethod
    def _candidate_order(tasks: list[TaskRecord], target_index: int) -> int | None:
        previous = tasks[target_index - 1] if target_index > 0 else None
        following = tasks[target_index + 1] if target_index + 1 < len(tasks) else None

        if previous is None and following is None:
            return 100
        if previous is None:
            if following is not None and following.order > 1:
                return max(1, following.order // 2)
            return None
        if following is None:
            return previous.order + 100

        gap = following.order - previous.order
        if gap > 1:
            return previous.order + gap // 2
        return None

    @staticmethod
    def _update_properties(
        path: Path,
        *,
        status: str | None,
        order: int,
    ) -> None:
        raw = path.read_bytes()
        has_bom = raw.startswith(_UTF8_BOM)
        text = raw.decode("utf-8-sig")
        lines = text.splitlines(keepends=True)
        status_replaced = status is None
        order_replaced = False

        for index, line in enumerate(lines):
            content = line.rstrip("\r\n")
            ending = line[len(content) :]
            if status is not None and not status_replaced and re.fullmatch(r"- status:\s*.*", content):
                lines[index] = f"- status: {status}{ending}"
                status_replaced = True
            elif not order_replaced and re.fullmatch(r"- order:\s*.*", content):
                lines[index] = f"- order: {order}{ending}"
                order_replaced = True

        if not status_replaced or not order_replaced:
            raise InvalidTaskError(f"Task properties changed before the move: {path.name}")

        encoded = "".join(lines).encode("utf-8")
        if has_bom:
            encoded = _UTF8_BOM + encoded

        temporary_path: Path | None = None
        try:
            with tempfile.NamedTemporaryFile(
                mode="wb",
                dir=path.parent,
                prefix=f".{path.name}.",
                suffix=".tmp",
                delete=False,
            ) as temporary:
                temporary.write(encoded)
                temporary.flush()
                os.fsync(temporary.fileno())
                temporary_path = Path(temporary.name)

            os.chmod(temporary_path, stat.S_IMODE(path.stat().st_mode))
            os.replace(temporary_path, path)
        finally:
            if temporary_path is not None and temporary_path.exists():
                temporary_path.unlink()
