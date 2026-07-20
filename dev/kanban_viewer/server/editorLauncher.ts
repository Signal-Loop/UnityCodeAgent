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
    const child = spawn(executable, ['--reuse-window', '--goto', taskPath], {
      detached: true,
      stdio: 'ignore',
      windowsHide: true,
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
