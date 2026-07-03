from __future__ import annotations

import json
import shutil
import subprocess
import time
from pathlib import Path
from typing import Any

import httpx

from .artifacts import EvalLogger
from .client import AgentServiceClient
from .models import EvalConfig, TelemetryConfig
from .paths import SERVICE_PROJECT_PATH, SKILLS_ROOT, set_managed_service_context
from .utils import get_value


class ManagedAgentService:
    def __init__(self, config: EvalConfig, logger: EvalLogger):
        self.config = config
        self.logger = logger
        self.project_root = logger.artifact_dir / "project-root"
        self.stdout_path = logger.artifact_dir / "service.stdout.log"
        self.stderr_path = logger.artifact_dir / "service.stderr.log"
        self.endpoint_manifest_path = self.project_root / ".unityCodeAgent" / "service" / "runtime" / "endpoint.json"
        self.process: subprocess.Popen[str] | None = None
        self.service_url: str | None = None
        self._stdout = None
        self._stderr = None

    def start(self) -> str:
        self._prepare_project_root()
        command = self.build_command(self.project_root, self.config.telemetry)
        self.logger.log(
            "managed_service_start",
            command=command,
            command_line=subprocess.list2cmdline(command),
            project_root=str(self.project_root),
            telemetry_enabled=self.config.telemetry.enabled,
            telemetry_capture_content=self.config.telemetry.capture_content,
            telemetry_otlp_endpoint=self.config.telemetry.otlp_endpoint,
        )
        self._stdout = self.stdout_path.open("w", encoding="utf-8")
        self._stderr = self.stderr_path.open("w", encoding="utf-8")
        self.process = subprocess.Popen(
            command,
            cwd=str(SERVICE_PROJECT_PATH.parent),
            stdout=self._stdout,
            stderr=self._stderr,
            text=True,
        )
        self.service_url = self._wait_for_service()
        set_managed_service_context(self.service_url, self.project_root)
        self.logger.log("managed_service_ready", service_url=self.service_url, process_id=self.process.pid)
        return self.service_url

    def stop(self) -> None:
        try:
            self.logger.log(
                "managed_service_stop_start",
                service_url=self.service_url,
                process_id=self.process.pid if self.process else None,
            )
            if self.service_url:
                try:
                    config = EvalConfig(
                        skill_name=self.config.skill_name,
                        service_url=self.service_url,
                        provider=self.config.provider,
                        telemetry=self.config.telemetry,
                        working_directory=self.project_root,
                        skill_directories=self.config.skill_directories,
                        disabled_skills=self.config.disabled_skills,
                        request_timeout_seconds=10,
                        scenario_timeout_seconds=10,
                        idle_timeout_seconds=10,
                        preflight_timeout_seconds=10,
                        tool_definitions=self.config.tool_definitions,
                        env_files_loaded=self.config.env_files_loaded,
                    )
                    client = AgentServiceClient(config, self.logger, timeout_seconds=10)
                    try:
                        client.stop_service()
                    finally:
                        client.close()
                except Exception as error:
                    self.logger.log("managed_service_stop_request_failed", error=repr(error))

            if self.process:
                try:
                    self.process.wait(timeout=20)
                    self.logger.log("managed_service_exited", exit_code=self.process.returncode, process_id=self.process.pid)
                except subprocess.TimeoutExpired:
                    self.logger.log("managed_service_terminate", process_id=self.process.pid)
                    self.process.terminate()
                    try:
                        self.process.wait(timeout=5)
                    except subprocess.TimeoutExpired:
                        self.logger.log("managed_service_kill", process_id=self.process.pid)
                        self.process.kill()
                        self.process.wait(timeout=5)
                self.logger.log(
                    "managed_service_stop_complete",
                    process_id=self.process.pid,
                    exit_code=self.process.returncode,
                )
        finally:
            set_managed_service_context(None, None)
            if self._stdout:
                self._stdout.close()
            if self._stderr:
                self._stderr.close()

    @staticmethod
    def build_command(project_root: Path, telemetry: TelemetryConfig) -> list[str]:
        artifacts_path = project_root.parent / "service-build"
        command = [
            "dotnet",
            "run",
            "--project",
            str(SERVICE_PROJECT_PATH),
            "--no-launch-profile",
            "--artifacts-path",
            str(artifacts_path),
            "-p:UseAppHost=false",
            "--",
            f"--ProjectRoot={project_root}",
            "--UnityProcessId=0",
            "--NoUnity=true",
            f"--EnableTelemetry={str(telemetry.enabled).lower()}",
            f"--TelemetryCaptureContent={str(telemetry.capture_content).lower()}",
            "--urls",
            "http://127.0.0.1:0",
        ]
        if telemetry.otlp_endpoint:
            command.append(f"--OtlpEndpoint={telemetry.otlp_endpoint}")
        return command

    def _prepare_project_root(self) -> None:
        if self.project_root.exists():
            shutil.rmtree(self.project_root)
        skills_target = self.project_root / ".agents" / "skills"
        skills_target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copytree(
            SKILLS_ROOT,
            skills_target,
            ignore=shutil.ignore_patterns("__pycache__", "*.pyc", ".env"),
        )

    def _wait_for_service(self) -> str:
        assert self.process is not None
        deadline = time.monotonic() + 90
        last_error: str | None = None
        while time.monotonic() < deadline:
            if self.process.poll() is not None:
                raise RuntimeError(
                    f"Managed service exited before it became healthy. exit_code={self.process.returncode}. "
                    f"stdout={self._tail(self.stdout_path)} stderr={self._tail(self.stderr_path)}"
                )

            manifest = self._read_endpoint_manifest()
            port = get_value(manifest, "Port", "port") if manifest else None
            if port:
                service_url = f"http://127.0.0.1:{port}"
                try:
                    with httpx.Client(base_url=service_url, timeout=2) as client:
                        response = client.get("/health")
                    if response.status_code < 500:
                        return service_url
                    last_error = f"health returned HTTP {response.status_code}"
                except Exception as error:
                    last_error = repr(error)
            time.sleep(0.25)

        raise TimeoutError(
            "Managed service did not publish a healthy endpoint within 90 seconds. "
            f"last_error={last_error} stdout={self._tail(self.stdout_path)} stderr={self._tail(self.stderr_path)}"
        )

    def _read_endpoint_manifest(self) -> dict[str, Any] | None:
        if not self.endpoint_manifest_path.exists():
            return None
        try:
            return json.loads(self.endpoint_manifest_path.read_text(encoding="utf-8"))
        except (json.JSONDecodeError, OSError):
            return None

    @staticmethod
    def _tail(path: Path, limit: int = 4000) -> str:
        if not path.exists():
            return ""
        text = path.read_text(encoding="utf-8", errors="replace")
        return text[-limit:].strip()

    @staticmethod
    def _redact_command(command: list[str]) -> list[str]:
        return ["--OtlpEndpoint=[REDACTED]" if item.startswith("--OtlpEndpoint=") else item for item in command]
