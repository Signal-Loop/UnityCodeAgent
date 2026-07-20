import { join } from 'node:path'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { createApp } from '../server/app.js'
import type { EditorLauncher } from '../server/editorLauncher.js'
import { writeFile } from 'node:fs/promises'
import { KanbanRepository, type FileOperations } from '../server/repository.js'
import { temporaryDirectory, writeTask } from './helpers.js'

const close: Array<() => Promise<void>> = []
afterEach(async () => Promise.all(close.splice(0).map((item) => item())))

describe('Fastify API', () => {
  it('preserves config, board, move, and editor routes', async () => {
    const temporary = await temporaryDirectory(); close.push(temporary.cleanup)
    const directory = join(temporary.path, 'docs', 'kanban')
    await writeTask(directory, 'task.md')
    const editor: EditorLauncher = { open: vi.fn(async () => undefined) }
    const app = createApp({ workspaceRoot: temporary.path, editorLauncher: editor })
    close.push(() => app.close())
    const config = await app.inject({ method: 'GET', url: '/api/config' })
    const board = await app.inject({ method: 'GET', url: '/api/board', query: { directory: 'docs/kanban' } })
    expect(config.statusCode).toBe(200)
    expect(board.json().tasks[0].title).toBe('task')
    const move = await app.inject({ method: 'PATCH', url: '/api/board/tasks', payload: { directory, task_path: 'task.md', target_status: 'Ready', target_index: 0, revision: board.json().revision } })
    expect(move.statusCode).toBe(200)
    expect(move.json().tasks[0].status).toBe('Ready')
    const opened = await app.inject({ method: 'POST', url: '/api/editor/open', payload: { directory, task_path: 'task.md' } })
    expect(opened.statusCode).toBe(204)
    expect(editor.open).toHaveBeenCalledWith(join(directory, 'task.md'))
  })

  it('maps invalid directories, stale boards, invalid requests, and launch failures', async () => {
    const temporary = await temporaryDirectory(); close.push(temporary.cleanup)
    await writeTask(temporary.path, 'task.md')
    const editor: EditorLauncher = { open: vi.fn(async () => { throw new Error('VS Code CLI missing') }) }
    const app = createApp({ workspaceRoot: temporary.path, editorLauncher: editor })
    close.push(() => app.close())
    expect((await app.inject({ method: 'GET', url: '/api/board', query: { directory: 'missing' } })).statusCode).toBe(400)
    const board = (await app.inject({ method: 'GET', url: '/api/board', query: { directory: temporary.path } })).json()
    expect((await app.inject({ method: 'PATCH', url: '/api/board/tasks', payload: { directory: temporary.path, task_path: 'task.md', target_status: 'Ready', target_index: -1, revision: board.revision } })).statusCode).toBe(422)
    expect((await app.inject({ method: 'PATCH', url: '/api/board/tasks', payload: { directory: temporary.path, task_path: 'task.md', target_status: 'Ready', target_index: 0, revision: 'stale' } })).statusCode).toBe(409)
    const opened = await app.inject({ method: 'POST', url: '/api/editor/open', payload: { directory: temporary.path, task_path: 'task.md' } })
    expect(opened.statusCode).toBe(503)
    expect(opened.json().detail).toBe('VS Code CLI missing')
    expect((await app.inject({ method: 'POST', url: '/api/editor/open', payload: { directory: temporary.path, task_path: '../task.md' } })).statusCode).toBe(422)
  })

  it('maps storage and unexpected failures without exposing content', async () => {
    const temporary = await temporaryDirectory(); close.push(temporary.cleanup)
    await writeTask(temporary.path, 'task.md')
    const files: FileOperations = { replace: vi.fn(async () => { throw Object.assign(new Error('secret content'), { code: 'EIO' }) }) }
    const repository = new KanbanRepository(temporary.path, files)
    const app = createApp({ workspaceRoot: temporary.path, repository })
    close.push(() => app.close())
    const board = (await app.inject({ method: 'GET', url: '/api/board', query: { directory: temporary.path } })).json()
    const failed = await app.inject({ method: 'PATCH', url: '/api/board/tasks', payload: { directory: temporary.path, task_path: 'task.md', target_status: 'Ready', target_index: 0, revision: board.revision } })
    expect(failed.statusCode).toBe(500)
    expect(failed.json()).toEqual({ detail: 'Could not commit task move.' })
    vi.spyOn(repository, 'loadBoard').mockRejectedValueOnce(new Error('unexpected secret'))
    const unexpected = await app.inject({ method: 'GET', url: '/api/board', query: { directory: temporary.path } })
    expect(unexpected.statusCode).toBe(500)
    expect(unexpected.body).not.toContain('unexpected secret')
  })

  it('streams retry, board-change, and heartbeat SSE frames', async () => {
    const temporary = await temporaryDirectory(); close.push(temporary.cleanup)
    await writeTask(temporary.path, 'task.md')
    const app = createApp({ workspaceRoot: temporary.path, heartbeatMs: 10 })
    await app.listen({ host: '127.0.0.1', port: 0 })
    close.push(() => app.close())
    const controller = new AbortController()
    const response = await fetch(`${app.listeningOrigin}/api/events?${new URLSearchParams({ directory: temporary.path })}`, { signal: controller.signal })
    const reader = response.body!.getReader()
    const decoder = new TextDecoder()
    let text = decoder.decode((await reader.read()).value)
    await new Promise((resolve) => setTimeout(resolve, 250))
    await writeFile(join(temporary.path, 'task.md'), '# Changed\n- status: Ready\n- order: 100\n')
    for (let attempt = 0; attempt < 60 && !text.includes('board-changed'); attempt += 1) {
      text += decoder.decode((await reader.read()).value)
    }
    controller.abort()
    expect(text).toContain('retry: 1000')
    expect(text).toContain(': heartbeat')
    expect(text).toContain('event: board-changed')
  })
})
