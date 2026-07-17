from __future__ import annotations

import asyncio
import shutil
import subprocess
from collections.abc import AsyncIterator
from pathlib import Path

from fastapi import FastAPI, HTTPException, Query, Response
from fastapi.responses import StreamingResponse
from pydantic import BaseModel, Field
from watchfiles import Change, awatch

from .kanban_repository import (
    STATUSES,
    BoardSnapshot,
    InvalidDirectoryError,
    InvalidTaskError,
    KanbanRepository,
    ParseWarning,
    StaleBoardError,
    TaskRecord,
)


class TaskDto(BaseModel):
    path: str
    title: str
    status: str
    order: int
    version: str


class WarningDto(BaseModel):
    path: str
    message: str


class BoardResponse(BaseModel):
    directory: str
    revision: str
    tasks: list[TaskDto]
    warnings: list[WarningDto]


class ConfigResponse(BaseModel):
    workspace_root: str
    default_task_directory: str
    statuses: list[str]


class MoveTaskRequest(BaseModel):
    directory: str
    task_path: str
    target_status: str
    target_index: int = Field(ge=0)
    revision: str


class OpenTaskRequest(BaseModel):
    directory: str
    task_path: str


class EditorLauncher:
    def open(self, task_file: Path) -> None:
        executable = shutil.which("code")
        if executable is None:
            raise FileNotFoundError("VS Code CLI 'code' was not found on PATH.")

        subprocess.Popen(
            [executable, "--reuse-window", "--goto", str(task_file)],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )


def _board_response(snapshot: BoardSnapshot) -> BoardResponse:
    return BoardResponse(
        directory=str(snapshot.directory),
        revision=snapshot.revision,
        tasks=[TaskDto(**_task_dict(task)) for task in snapshot.tasks],
        warnings=[WarningDto(**_warning_dict(warning)) for warning in snapshot.warnings],
    )


def _task_dict(task: TaskRecord) -> dict[str, str | int]:
    return {
        "path": task.path,
        "title": task.title,
        "status": task.status,
        "order": task.order,
        "version": task.version,
    }


def _warning_dict(warning: ParseWarning) -> dict[str, str]:
    return {"path": warning.path, "message": warning.message}


def create_app(
    *,
    workspace_root: Path | None = None,
    repository: KanbanRepository | None = None,
    editor_launcher: EditorLauncher | None = None,
) -> FastAPI:
    root = (workspace_root or Path(__file__).resolve().parents[3]).resolve()
    kanban_repository = repository or KanbanRepository(root)
    launcher = editor_launcher or EditorLauncher()
    app = FastAPI(title="Kanban Task Viewer API", version="0.1.0")

    @app.get("/api/config", response_model=ConfigResponse)
    def get_config() -> ConfigResponse:
        return ConfigResponse(
            workspace_root=str(root),
            default_task_directory=str(root / "docs" / "kanban"),
            statuses=list(STATUSES),
        )

    @app.get("/api/board", response_model=BoardResponse)
    def get_board(directory: str = Query(..., min_length=1)) -> BoardResponse:
        try:
            return _board_response(kanban_repository.load_board(directory))
        except InvalidDirectoryError as error:
            raise HTTPException(status_code=400, detail=str(error)) from error

    @app.patch("/api/board/tasks", response_model=BoardResponse)
    def move_task(request: MoveTaskRequest) -> BoardResponse:
        try:
            snapshot = kanban_repository.move_task(
                request.directory,
                request.task_path,
                request.target_status,
                request.target_index,
                request.revision,
            )
            return _board_response(snapshot)
        except InvalidDirectoryError as error:
            raise HTTPException(status_code=400, detail=str(error)) from error
        except StaleBoardError as error:
            raise HTTPException(status_code=409, detail=str(error)) from error
        except InvalidTaskError as error:
            raise HTTPException(status_code=422, detail=str(error)) from error

    @app.post("/api/editor/open", status_code=204)
    def open_task(request: OpenTaskRequest) -> Response:
        try:
            directory = kanban_repository.resolve_directory(request.directory)
            task_file = kanban_repository.resolve_task_path(directory, request.task_path)
            launcher.open(task_file)
        except InvalidDirectoryError as error:
            raise HTTPException(status_code=400, detail=str(error)) from error
        except InvalidTaskError as error:
            raise HTTPException(status_code=422, detail=str(error)) from error
        except FileNotFoundError as error:
            raise HTTPException(status_code=503, detail=str(error)) from error
        return Response(status_code=204)

    @app.get("/api/events")
    async def events(directory: str = Query(..., min_length=1)) -> StreamingResponse:
        try:
            resolved = kanban_repository.resolve_directory(directory)
        except InvalidDirectoryError as error:
            raise HTTPException(status_code=400, detail=str(error)) from error

        async def stream() -> AsyncIterator[str]:
            yield "retry: 1000\n\n"
            try:
                async for changes in awatch(resolved, debounce=150, step=50):
                    if _contains_board_change(changes):
                        yield "event: board-changed\ndata: refresh\n\n"
                    await asyncio.sleep(0)
            except asyncio.CancelledError:
                return

        return StreamingResponse(
            stream(),
            media_type="text/event-stream",
            headers={
                "Cache-Control": "no-cache",
                "X-Accel-Buffering": "no",
            },
        )

    return app


def _contains_board_change(changes: set[tuple[Change, str]]) -> bool:
    return any(
        Path(path).suffix.lower() == ".md" and Path(path).name.lower() != "readme.md"
        for _, path in changes
    )


app = create_app()
