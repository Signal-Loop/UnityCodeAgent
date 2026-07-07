# SKILLS

## Bundled Skills

Package-bundled skills live in `Packages/com.signal-loop.unitycodeagent/Editor/Skills~`. Keep `unitycodeagent` and `unity-game-player` synced with the published skills repository at `C:\Users\tbory\source\Workspaces\agentskills`.

Install or update the bundled package skills through the `skills` package, then copy the installed Codex skill output into the package bundle:

```powershell
function Sync-Skill($source, $destination) {
    robocopy $source $destination /MIR
    if ($LASTEXITCODE -gt 7) { throw "robocopy failed with exit code $LASTEXITCODE" }
    $global:LASTEXITCODE = 0
}

$tempRoot = Join-Path $env:TEMP "unitycodeagent-skills-install"
$installedSkills = Join-Path $tempRoot ".agents\skills"
$packageSkills = "C:\Users\tbory\source\Workspaces\Loop\UnityCodeAgent\Packages\com.signal-loop.unitycodeagent\Editor\Skills~"
Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
Push-Location $tempRoot
try {
    npx --yes skills add signal-loop/agentskills --agent codex --skill unitycodeagent --skill unity-game-player -y
} finally {
    Pop-Location
}
Sync-Skill "$installedSkills\unitycodeagent" "$packageSkills\unitycodeagent"
Sync-Skill "$installedSkills\unity-game-player" "$packageSkills\unity-game-player"
```

Reverse the operation by updating the `agentskills` repository from this package and pushing the result:

```powershell
function Sync-Skill($source, $destination) {
    robocopy $source $destination /MIR
    if ($LASTEXITCODE -gt 7) { throw "robocopy failed with exit code $LASTEXITCODE" }
    $global:LASTEXITCODE = 0
}

$packageSkills = "C:\Users\tbory\source\Workspaces\Loop\UnityCodeAgent\Packages\com.signal-loop.unitycodeagent\Editor\Skills~"
$repoRoot = "C:\Users\tbory\source\Workspaces\agentskills"
$repoSkills = "$repoRoot\skills"
Sync-Skill "$packageSkills\unitycodeagent" "$repoSkills\unitycodeagent"
Sync-Skill "$packageSkills\unity-game-player" "$repoSkills\unity-game-player"
git -C "$repoRoot" status --short
git -C "$repoRoot" add skills/unitycodeagent skills/unity-game-player
git -C "$repoRoot" diff --cached --quiet
if ($LASTEXITCODE -eq 0) {
    Write-Host "No agentskills changes to publish."
} else {
    git -C "$repoRoot" commit -m "Update UnityCodeAgent bundled skills"
    git -C "$repoRoot" push
}
```
