import { chmod, readFile, unlink, writeFile } from 'node:fs/promises'
import { delimiter, dirname, join } from 'node:path'
import { afterEach, describe, expect, it } from 'vitest'
import { VsCodeEditorLauncher } from '../server/editorLauncher.js'
import { temporaryDirectory } from './helpers.js'

const cleanups: Array<() => Promise<void>> = []
afterEach(async () => Promise.all(cleanups.splice(0).map((cleanup) => cleanup())))

describe('VsCodeEditorLauncher', () => {
  it('reports a missing executable', async () => {
    await expect(new VsCodeEditorLauncher({ PATH: '' }).open('task.md')).rejects.toThrow("VS Code CLI 'code' was not found")
  })

  it.runIf(process.platform !== 'win32')('launches code from PATH without a shell command', async () => {
    const temporary = await temporaryDirectory(); cleanups.push(temporary.cleanup)
    const executable = join(temporary.path, 'code')
    await writeFile(executable, '#!/bin/sh\nexit 0\n')
    await chmod(executable, 0o755)
    await expect(new VsCodeEditorLauncher({ PATH: [temporary.path, '/missing'].join(delimiter) }).open('task with spaces.md')).resolves.toBeUndefined()
  })

  it.runIf(process.platform === 'win32')('launches the code command script from PATH', async () => {
    const temporary = await temporaryDirectory(); cleanups.push(temporary.cleanup)
    const invocation = join(temporary.path, 'invocation.txt')
    await writeFile(join(temporary.path, 'code.cmd'), `@echo %*>"${invocation}"\r\n`)

    const commandShell = process.env.ComSpec ?? 'C:\\Windows\\System32\\cmd.exe'
    const environment = { PATH: temporary.path, ComSpec: commandShell }
    await expect(new VsCodeEditorLauncher(environment).open('task with spaces.md')).resolves.toBeUndefined()
    await expect.poll(async () => (await readFile(invocation, 'utf8')).trim()).toBe('--reuse-window --goto "task with spaces.md"')

    await unlink(invocation)
    const fallbackEnvironment = { PATH: [temporary.path, dirname(commandShell)].join(delimiter) }
    await expect(new VsCodeEditorLauncher(fallbackEnvironment).open('task with spaces.md')).resolves.toBeUndefined()
    await expect.poll(async () => (await readFile(invocation, 'utf8')).trim()).toBe('--reuse-window --goto "task with spaces.md"')
  })
})
