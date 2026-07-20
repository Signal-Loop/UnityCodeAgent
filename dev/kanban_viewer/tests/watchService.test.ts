import { EventEmitter } from 'node:events'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { FSWatcher } from 'chokidar'
import { BoardWatchService } from '../server/watchService.js'

class FakeWatcher extends EventEmitter {
  close = vi.fn(async () => undefined)
}

afterEach(() => vi.useRealTimers())

describe('BoardWatchService', () => {
  it('shares watchers, filters events, debounces bursts, and closes after the final subscriber', async () => {
    vi.useFakeTimers()
    const watcher = new FakeWatcher()
    const factory = vi.fn(() => watcher as unknown as FSWatcher)
    const service = new BoardWatchService(factory)
    const first = vi.fn(); const second = vi.fn()
    const unsubscribeFirst = service.subscribe('/board', first)
    const unsubscribeSecond = service.subscribe('/board', second)
    expect(factory).toHaveBeenCalledTimes(1)
    watcher.emit('change', '/board/README.md')
    watcher.emit('change', '/board/task.md')
    watcher.emit('add', '/board/task.md')
    await vi.advanceTimersByTimeAsync(150)
    expect(first).toHaveBeenCalledTimes(1)
    expect(second).toHaveBeenCalledTimes(1)
    unsubscribeFirst()
    expect(watcher.close).not.toHaveBeenCalled()
    unsubscribeSecond()
    expect(watcher.close).toHaveBeenCalledTimes(1)
  })

  it('reports watcher errors and closes all active watchers', async () => {
    const watchers = [new FakeWatcher(), new FakeWatcher()]
    const onError = vi.fn()
    const service = new BoardWatchService(() => watchers.shift() as unknown as FSWatcher, onError)
    service.subscribe('/one', vi.fn())
    service.subscribe('/two', vi.fn())
    watchers.length = 0
    const active = service.activeWatcherCount
    expect(active).toBe(2)
    // The created watcher is reachable through the factory return before it is shifted.
    const errorWatcher = new FakeWatcher()
    const errorService = new BoardWatchService(() => errorWatcher as unknown as FSWatcher, onError)
    errorService.subscribe('/error', vi.fn())
    const error = new Error('watch failed')
    errorWatcher.emit('error', error)
    expect(onError).toHaveBeenCalledWith('/error', error)
    await errorService.close()
    await service.close()
    expect(service.activeWatcherCount).toBe(0)
  })
})
