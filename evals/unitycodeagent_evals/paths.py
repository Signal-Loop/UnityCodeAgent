from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SKILLS_ROOT = ROOT / "Packages" / "com.signal-loop.unitycodeagent" / "Editor" / "Skills~"
DEFAULT_ENDPOINT_MANIFEST = ROOT / ".unityCodeAgent" / "service" / "runtime" / "endpoint.json"
ARTIFACT_ROOT = ROOT / "evals" / ".artifacts"
SERVICE_PROJECT_PATH = (
    ROOT
    / "Packages"
    / "com.signal-loop.unitycodeagent"
    / "Editor"
    / "CopilotService~"
    / "UnityCodeCopilot.Service.csproj"
)

_managed_service_url: str | None = None
_managed_working_directory: Path | None = None


def set_managed_service_context(service_url: str | None, working_directory: Path | None) -> None:
    global _managed_service_url, _managed_working_directory
    _managed_service_url = service_url.rstrip("/") if service_url else None
    _managed_working_directory = working_directory.resolve() if working_directory else None


def get_managed_service_url() -> str | None:
    return _managed_service_url


def get_managed_working_directory() -> Path | None:
    return _managed_working_directory
