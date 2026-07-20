from __future__ import annotations

from pathlib import Path

import pytest

from backend.kanban_repository import (
    InvalidTaskError,
    KanbanRepository,
    StaleBoardError,
)


def write_task(
    root: Path,
    name: str,
    *,
    title: str,
    status: str,
    order: int,
    newline: str = "\n",
    bom: bool = False,
) -> Path:
    path = root / name
    path.parent.mkdir(parents=True, exist_ok=True)
    text = newline.join(
        (
            f"# {title}",
            "",
            f"- status: {status}",
            f"- order: {order}",
            "",
            "Body stays unchanged.",
            "",
        )
    )
    encoded = text.encode("utf-8")
    path.write_bytes((b"\xef\xbb\xbf" if bom else b"") + encoded)
    return path


def test_load_board_recurses_sorts_and_reports_invalid_files(tmp_path: Path) -> None:
    write_task(tmp_path, "z.md", title="Second", status="Backlog", order=200)
    write_task(tmp_path, "nested/a.md", title="First", status="Backlog", order=100)
    write_task(tmp_path, "done.md", title="Done", status="Completed", order=100)
    (tmp_path / "README.md").write_text("not a task", encoding="utf-8")
    (tmp_path / "broken.md").write_text("# Broken\n- status: Backlog\n", encoding="utf-8")

    board = KanbanRepository(tmp_path).load_board(tmp_path)

    assert [task.title for task in board.tasks] == ["First", "Second", "Done"]
    assert [(warning.path, warning.message) for warning in board.warnings] == [
        ("broken.md", "Expected exactly one '- order:' property.")
    ]


def test_load_board_reads_optional_goal(tmp_path: Path) -> None:
    task = write_task(tmp_path, "task.md", title="Task", status="Backlog", order=100)
    task.write_text(
        task.read_text(encoding="utf-8").replace(
            "- order: 100", "- order: 100\n- goal: Display the task goal on its card."
        ),
        encoding="utf-8",
    )

    board = KanbanRepository(tmp_path).load_board(tmp_path)

    assert board.tasks[0].goal == "Display the task goal on its card."


def test_duplicate_orders_are_stable_and_warned(tmp_path: Path) -> None:
    write_task(tmp_path, "b.md", title="B", status="Ready", order=100)
    write_task(tmp_path, "a.md", title="A", status="Ready", order=100)

    board = KanbanRepository(tmp_path).load_board(tmp_path)

    assert [task.path for task in board.tasks] == ["a.md", "b.md"]
    assert len(board.warnings) == 2
    assert all("Duplicate order 100 in Ready" in warning.message for warning in board.warnings)


def test_move_between_columns_uses_available_gap(tmp_path: Path) -> None:
    write_task(tmp_path, "move.md", title="Move", status="Backlog", order=100)
    write_task(tmp_path, "one.md", title="One", status="Started", order=100)
    write_task(tmp_path, "two.md", title="Two", status="Started", order=200)
    repository = KanbanRepository(tmp_path)
    before = repository.load_board(tmp_path)

    after = repository.move_task(tmp_path, "move.md", "Started", 1, before.revision)

    moved = next(task for task in after.tasks if task.path == "move.md")
    assert (moved.status, moved.order) == ("Started", 150)
    assert "Body stays unchanged." in (tmp_path / "move.md").read_text(encoding="utf-8")


def test_reorder_within_a_column_updates_only_the_moved_task(tmp_path: Path) -> None:
    write_task(tmp_path, "one.md", title="One", status="Backlog", order=100)
    write_task(tmp_path, "two.md", title="Two", status="Backlog", order=200)
    write_task(tmp_path, "three.md", title="Three", status="Backlog", order=300)
    repository = KanbanRepository(tmp_path)
    before = repository.load_board(tmp_path)

    after = repository.move_task(tmp_path, "three.md", "Backlog", 0, before.revision)

    backlog = [task for task in after.tasks if task.status == "Backlog"]
    assert [(task.path, task.order) for task in backlog] == [
        ("three.md", 50),
        ("one.md", 100),
        ("two.md", 200),
    ]


def test_move_normalizes_only_target_column_when_no_gap(tmp_path: Path) -> None:
    write_task(tmp_path, "move.md", title="Move", status="Backlog", order=50)
    write_task(tmp_path, "one.md", title="One", status="Ready", order=1)
    write_task(tmp_path, "two.md", title="Two", status="Ready", order=2)
    repository = KanbanRepository(tmp_path)
    before = repository.load_board(tmp_path)

    after = repository.move_task(tmp_path, "move.md", "Ready", 1, before.revision)

    ready = [task for task in after.tasks if task.status == "Ready"]
    assert [(task.path, task.order) for task in ready] == [
        ("one.md", 100),
        ("move.md", 200),
        ("two.md", 300),
    ]


def test_move_preserves_bom_crlf_and_unrelated_content(tmp_path: Path) -> None:
    path = write_task(
        tmp_path,
        "task.md",
        title="Task",
        status="Backlog",
        order=100,
        newline="\r\n",
        bom=True,
    )
    write_task(tmp_path, "target.md", title="Target", status="Started", order=100)
    repository = KanbanRepository(tmp_path)
    before = repository.load_board(tmp_path)

    repository.move_task(tmp_path, "task.md", "Started", 1, before.revision)
    raw = path.read_bytes()

    assert raw.startswith(b"\xef\xbb\xbf")
    assert b"\r\n" in raw
    assert b"Body stays unchanged.\r\n" in raw


def test_move_rejects_stale_revision(tmp_path: Path) -> None:
    path = write_task(tmp_path, "task.md", title="Task", status="Backlog", order=100)
    repository = KanbanRepository(tmp_path)
    before = repository.load_board(tmp_path)
    path.write_text(path.read_text(encoding="utf-8") + "External edit\n", encoding="utf-8")

    with pytest.raises(StaleBoardError):
        repository.move_task(tmp_path, "task.md", "Started", 0, before.revision)


def test_task_path_cannot_escape_selected_directory(tmp_path: Path) -> None:
    root = tmp_path / "board"
    root.mkdir()
    write_task(tmp_path, "outside.md", title="Outside", status="Backlog", order=100)
    repository = KanbanRepository(tmp_path)

    with pytest.raises(InvalidTaskError):
        repository.resolve_task_path(root, "../outside.md")


def test_task_symlink_cannot_escape_selected_directory(tmp_path: Path) -> None:
    root = tmp_path / "board"
    root.mkdir()
    outside = write_task(tmp_path, "outside.md", title="Outside", status="Backlog", order=100)
    link = root / "linked.md"
    try:
        link.symlink_to(outside)
    except OSError:
        pytest.skip("Creating symlinks is not permitted on this machine.")
    repository = KanbanRepository(tmp_path)

    with pytest.raises(InvalidTaskError):
        repository.resolve_task_path(root, "linked.md")
