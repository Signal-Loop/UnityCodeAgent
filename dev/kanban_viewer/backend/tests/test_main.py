from __future__ import annotations

from pathlib import Path

from fastapi.testclient import TestClient

from backend.main import create_app


class FakeEditorLauncher:
    def __init__(self, error: Exception | None = None) -> None:
        self.error = error
        self.opened: list[Path] = []

    def open(self, task_file: Path) -> None:
        if self.error is not None:
            raise self.error
        self.opened.append(task_file)


def write_task(root: Path, name: str = "task.md") -> Path:
    path = root / name
    path.write_text("# Task\n\n- status: Backlog\n- order: 100\n", encoding="utf-8")
    return path


def test_config_and_board_endpoints(tmp_path: Path) -> None:
    board = tmp_path / "docs" / "kanban"
    board.mkdir(parents=True)
    write_task(board)
    client = TestClient(create_app(workspace_root=tmp_path))

    config = client.get("/api/config")
    response = client.get("/api/board", params={"directory": "docs/kanban"})

    assert config.status_code == 200
    assert config.json()["default_task_directory"] == str(board)
    assert response.status_code == 200
    assert response.json()["tasks"][0]["title"] == "Task"


def test_move_endpoint_returns_conflict_for_stale_revision(tmp_path: Path) -> None:
    board = tmp_path / "board"
    board.mkdir()
    path = write_task(board)
    client = TestClient(create_app(workspace_root=tmp_path))
    snapshot = client.get("/api/board", params={"directory": str(board)}).json()
    path.write_text(path.read_text(encoding="utf-8") + "changed", encoding="utf-8")

    response = client.patch(
        "/api/board/tasks",
        json={
            "directory": str(board),
            "task_path": "task.md",
            "target_status": "Ready",
            "target_index": 0,
            "revision": snapshot["revision"],
        },
    )

    assert response.status_code == 409


def test_open_endpoint_uses_validated_task_path(tmp_path: Path) -> None:
    board = tmp_path / "board"
    board.mkdir()
    task = write_task(board)
    launcher = FakeEditorLauncher()
    client = TestClient(create_app(workspace_root=tmp_path, editor_launcher=launcher))

    response = client.post(
        "/api/editor/open",
        json={"directory": str(board), "task_path": "task.md"},
    )

    assert response.status_code == 204
    assert launcher.opened == [task]


def test_open_endpoint_reports_missing_vscode(tmp_path: Path) -> None:
    board = tmp_path / "board"
    board.mkdir()
    write_task(board)
    launcher = FakeEditorLauncher(FileNotFoundError("VS Code CLI missing"))
    client = TestClient(create_app(workspace_root=tmp_path, editor_launcher=launcher))

    response = client.post(
        "/api/editor/open",
        json={"directory": str(board), "task_path": "task.md"},
    )

    assert response.status_code == 503
    assert response.json()["detail"] == "VS Code CLI missing"
