import { spawn } from 'node:child_process'
import { constants } from 'node:fs'
import { access } from 'node:fs/promises'
import { delimiter, join } from 'node:path'

export interface EditorLauncher {
  open(taskPath: string): Promise<void>
}

export class VsCodeEditorLauncher implements EditorLauncher {
  constructor(private readonly environment: NodeJS.ProcessEnv = process.env) {}

  async open(taskPath: string): Promise<void> {
    const executable = await findCode(this.environment)
    if (!executable) throw new Error("VS Code CLI 'code' was not found on PATH.")

    const commandScript = process.platform === 'win32' && /\.(?:cmd|bat)$/i.test(executable)
    const commandShell = this.environment.ComSpec ?? 'cmd.exe'
    const editorCommandVariable = 'KANBAN_VIEWER_EDITOR_COMMAND'
    const taskPathVariable = 'KANBAN_VIEWER_EDITOR_TASK_PATH'
    const command = commandScript
      ? `"%${editorCommandVariable}%" --reuse-window --goto "%${taskPathVariable}%"`
      : executable
    const child = spawn(command, commandScript ? [] : ['--reuse-window', '--goto', taskPath], {
      detached: true,
      stdio: 'ignore',
      windowsHide: true,
      shell: commandScript ? commandShell : false,
      env: commandScript ? {
        ...process.env,
        ...this.environment,
        [editorCommandVariable]: executable,
        [taskPathVariable]: taskPath,
      } : undefined,
    })
    child.unref()
  }
}

async function findCode(environment: NodeJS.ProcessEnv): Promise<string | undefined> {
  const names = process.platform === 'win32' ? ['code.cmd', 'code.exe', 'code.bat'] : ['code']
  for (const directory of (environment.PATH ?? '').split(delimiter).filter(Boolean)) {
    for (const name of names) {
      const candidate = join(directory, name)
      try { await access(candidate, constants.X_OK); return candidate } catch { /* keep searching */ }
    }
  }
  return undefined
}
