import chokidar, { type FSWatcher } from 'chokidar'

export type WatcherFactory = (directory: string) => FSWatcher

interface SharedWatcher {
  watcher: FSWatcher
  subscribers: Set<() => void>
  timer?: ReturnType<typeof setTimeout>
}

export class BoardWatchService {
  private readonly watchers = new Map<string, SharedWatcher>()

  constructor(
    private readonly createWatcher: WatcherFactory = (directory) => chokidar.watch(directory, {
      ignoreInitial: true,
      followSymlinks: false,
      awaitWriteFinish: { stabilityThreshold: 100, pollInterval: 20 },
    }),
    private readonly onError: (directory: string, error: unknown) => void = () => undefined,
  ) {}

  subscribe(directory: string, listener: () => void): () => void {
    let shared = this.watchers.get(directory)
    if (!shared) {
      const watcher = this.createWatcher(directory)
      shared = { watcher, subscribers: new Set() }
      const notify = (path: string) => {
        if (!isTaskMarkdown(path)) return
        if (shared!.timer) clearTimeout(shared!.timer)
        shared!.timer = setTimeout(() => {
          shared!.timer = undefined
          for (const subscriber of shared!.subscribers) subscriber()
        }, 150)
      }
      watcher.on('add', notify).on('change', notify).on('unlink', notify).on('error', (error) => this.onError(directory, error))
      this.watchers.set(directory, shared)
    }
    shared.subscribers.add(listener)
    return () => {
      shared!.subscribers.delete(listener)
      if (shared!.subscribers.size) return
      if (shared!.timer) clearTimeout(shared!.timer)
      this.watchers.delete(directory)
      void shared!.watcher.close()
    }
  }

  async close(): Promise<void> {
    const values = [...this.watchers.values()]
    this.watchers.clear()
    for (const shared of values) {
      if (shared.timer) clearTimeout(shared.timer)
      await shared.watcher.close()
    }
  }

  get activeWatcherCount(): number { return this.watchers.size }
}

function isTaskMarkdown(path: string): boolean {
  const name = path.replaceAll('\\', '/').split('/').at(-1)?.toLowerCase()
  return name?.endsWith('.md') === true && name !== 'readme.md'
}
