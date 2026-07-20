import { mkdtemp, mkdir, rm, writeFile } from 'node:fs/promises'
import { dirname, join } from 'node:path'
import { tmpdir } from 'node:os'

export async function temporaryDirectory(): Promise<{ path: string; cleanup: () => Promise<void> }> {
  const path = await mkdtemp(join(tmpdir(), 'kanban-viewer-'))
  return { path, cleanup: () => rm(path, { recursive: true, force: true }) }
}

export async function writeTask(root: string, name: string, status = 'Backlog', order = 100, options?: { bom?: boolean; newline?: string }): Promise<string> {
  const path = join(root, name)
  await mkdir(dirname(path), { recursive: true })
  const newline = options?.newline ?? '\n'
  const body = [`# ${name.replace('.md', '')}`, '', `- status: ${status}`, `- order: ${order}`, '', 'Body stays unchanged.', ''].join(newline)
  await writeFile(path, Buffer.concat([options?.bom ? Buffer.from([0xef, 0xbb, 0xbf]) : Buffer.alloc(0), Buffer.from(body)]))
  return path
}
