import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError, eventsUrl, getBoard, getConfig, moveTask, openTask } from './api'

afterEach(() => vi.restoreAllMocks())

describe('API client', () => {
  it('encodes task directories and move requests', async () => {
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(new Response(JSON.stringify({ tasks: [] }), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ tasks: [] }), { status: 200 }))

    await getBoard('C:\\work space\\docs\\kanban')
    await moveTask({
      directory: 'board',
      task_path: 'task.md',
      target_status: 'Ready',
      target_index: 0,
      revision: 'revision',
    })

    expect(String(fetchMock.mock.calls[0][0])).toContain('directory=C%3A%5Cwork+space')
    expect(fetchMock.mock.calls[1][1]?.method).toBe('PATCH')
    expect(fetchMock.mock.calls[1][1]?.body).toContain('"target_status":"Ready"')
    expect(eventsUrl('a b')).toBe('/api/events?directory=a+b')
  })

  it('surfaces backend detail messages and accepts 204 open responses', async () => {
    vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(
        new Response(JSON.stringify({ detail: 'Board changed.' }), {
          status: 409,
          headers: { 'Content-Type': 'application/json' },
        }),
      )
      .mockResolvedValueOnce(new Response(null, { status: 204 }))

    await expect(getBoard('board')).rejects.toEqual(new ApiError('Board changed.', 409))
    await expect(openTask('board', 'task.md')).resolves.toBeUndefined()
  })

  it('loads config and falls back for non-JSON errors', async () => {
    vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(new Response(JSON.stringify({ statuses: [] }), { status: 200 }))
      .mockResolvedValueOnce(new Response('bad gateway', { status: 502 }))
    await expect(getConfig()).resolves.toEqual({ statuses: [] })
    await expect(getBoard('board')).rejects.toEqual(new ApiError('Request failed with status 502.', 502))
  })
})
