import { useCallback, useEffect, useRef, useState } from 'react'
import {
  eventsUrl,
  getBoard,
  getConfig,
  moveTask,
  openTask,
  type BoardResponse,
  type ConfigResponse,
  type KanbanStatus,
  type TaskDto,
} from './api'
import { reorderTasks } from './boardState'
import { KanbanBoard } from './components/KanbanBoard'

const STORAGE_KEY = 'kanban-viewer.task-directory'

type WatcherState = 'connecting' | 'connected' | 'disconnected'

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : 'An unexpected error occurred.'
}

export default function App() {
  const [config, setConfig] = useState<ConfigResponse | null>(null)
  const [board, setBoard] = useState<BoardResponse | null>(null)
  const [folderInput, setFolderInput] = useState('')
  const [activeDirectory, setActiveDirectory] = useState('')
  const [error, setError] = useState('')
  const [isLoading, setIsLoading] = useState(true)
  const [isMoving, setIsMoving] = useState(false)
  const [watcherState, setWatcherState] = useState<WatcherState>('connecting')
  const [watcherRefreshRequest, setWatcherRefreshRequest] = useState(0)
  const movingRef = useRef(false)
  const generationRef = useRef(0)
  const activeDirectoryRef = useRef('')
  const boardRequestRef = useRef<AbortController | null>(null)
  const pendingWatcherRefreshRef = useRef(false)

  const loadBoard = useCallback(async (directory: string, activate = false) => {
    const generation = activate ? ++generationRef.current : generationRef.current
    boardRequestRef.current?.abort()
    const controller = new AbortController()
    boardRequestRef.current = controller
    const nextBoard = await getBoard(directory, controller.signal).finally(() => {
      if (boardRequestRef.current === controller) boardRequestRef.current = null
    })
    if (generation !== generationRef.current) return null
    setBoard(nextBoard)
    setError('')
    if (activate) {
      setWatcherState('connecting')
      setActiveDirectory(nextBoard.directory)
      activeDirectoryRef.current = nextBoard.directory
      setFolderInput(nextBoard.directory)
      localStorage.setItem(STORAGE_KEY, nextBoard.directory)
    }
    if (!activate && pendingWatcherRefreshRef.current && !movingRef.current && activeDirectoryRef.current) {
      pendingWatcherRefreshRef.current = false
      setWatcherRefreshRequest((request) => request + 1)
    }
    return nextBoard
  }, [])

  useEffect(() => {
    if (!watcherRefreshRequest || !activeDirectoryRef.current) return
    void loadBoard(activeDirectoryRef.current).catch((watchError) => setError(errorMessage(watchError)))
  }, [loadBoard, watcherRefreshRequest])

  useEffect(() => {
    let cancelled = false
    const initialize = async () => {
      try {
        const nextConfig = await getConfig()
        if (cancelled) return
        setConfig(nextConfig)
        const savedDirectory = localStorage.getItem(STORAGE_KEY)
        const initialDirectory = savedDirectory || nextConfig.default_task_directory
        try {
          await loadBoard(initialDirectory, true)
        } catch (savedError) {
          if (!savedDirectory) throw savedError
          localStorage.removeItem(STORAGE_KEY)
          await loadBoard(nextConfig.default_task_directory, true)
        }
      } catch (initializationError) {
        if (!cancelled) setError(errorMessage(initializationError))
      } finally {
        if (!cancelled) setIsLoading(false)
      }
    }
    void initialize()
    return () => {
      cancelled = true
      generationRef.current += 1
      boardRequestRef.current?.abort()
    }
  }, [loadBoard])

  useEffect(() => {
    if (!activeDirectory) return
    const generation = generationRef.current
    const source = new EventSource(eventsUrl(activeDirectory))
    source.onopen = () => { if (generation === generationRef.current) setWatcherState('connected') }
    source.onerror = () => { if (generation === generationRef.current) setWatcherState('disconnected') }
    source.addEventListener('board-changed', () => {
      if (generation !== generationRef.current) return
      if (movingRef.current || boardRequestRef.current) {
        pendingWatcherRefreshRef.current = true
        return
      }
      void loadBoard(activeDirectory).catch((watchError) => setError(errorMessage(watchError)))
    })
    return () => source.close()
  }, [activeDirectory, loadBoard])

  const applyFolder = async () => {
    if (!folderInput.trim()) {
      setError('Enter a task folder path.')
      return
    }
    setIsLoading(true)
    try {
      await loadBoard(folderInput.trim(), true)
    } catch (folderError) {
      setError(errorMessage(folderError))
    } finally {
      setIsLoading(false)
    }
  }

  const refreshBoard = async () => {
    if (!activeDirectory) return
    setIsLoading(true)
    try {
      await loadBoard(activeDirectory)
    } catch (refreshError) {
      setError(errorMessage(refreshError))
    } finally {
      setIsLoading(false)
    }
  }

  const handleMove = async (
    taskPath: string,
    targetStatus: KanbanStatus,
    targetIndex: number,
  ) => {
    if (!board || movingRef.current || !config) return
    const generation = generationRef.current
    const previousBoard = board
    const optimisticBoard: BoardResponse = {
      ...board,
      tasks: reorderTasks(board.tasks, taskPath, targetStatus, targetIndex, config.statuses),
    }
    setBoard(optimisticBoard)
    setError('')
    setIsMoving(true)
    movingRef.current = true
    try {
      const updated = await moveTask({
        directory: board.directory,
        task_path: taskPath,
        target_status: targetStatus,
        target_index: targetIndex,
        revision: board.revision,
      })
      if (generation === generationRef.current) setBoard(updated)
    } catch (moveError) {
      if (generation !== generationRef.current) return
      const mutationError = errorMessage(moveError)
      try {
        const authoritative = await loadBoard(previousBoard.directory)
        if (!authoritative && generation === generationRef.current) setBoard(previousBoard)
      } catch {
        if (generation === generationRef.current) setBoard(previousBoard)
      }
      if (generation === generationRef.current) setError(mutationError)
    } finally {
      movingRef.current = false
      if (generation === generationRef.current) {
        setIsMoving(false)
        if (pendingWatcherRefreshRef.current) {
          pendingWatcherRefreshRef.current = false
          void loadBoard(activeDirectoryRef.current).catch((watchError) => setError(errorMessage(watchError)))
        }
      }
    }
  }

  const handleOpen = async (task: TaskDto) => {
    if (!board) return
    try {
      await openTask(board.directory, task.path)
      setError('')
    } catch (openError) {
      setError(errorMessage(openError))
    }
  }

  const counts = config?.statuses.map(
    (status) => `${board?.tasks.filter((task) => task.status === status).length ?? 0} ${status}`,
  )

  return (
    <main className="mx-auto min-h-screen max-w-[1600px] px-5 py-8 sm:px-8 sm:py-12">
      <header className="mb-7 max-w-3xl">
        <p className="mb-2 font-mono text-[11px] uppercase tracking-[0.12em] text-[var(--clay-d)]">
          Workspace / docs / kanban
        </p>
        <h1 className="font-serif text-4xl font-medium tracking-[-0.025em] sm:text-5xl">
          Kanban task viewer
        </h1>
        <p className="mt-3 max-w-2xl text-[14px] leading-6 text-[var(--gray-800)]">
          Move Markdown tasks through the workflow, reorder priorities, or open a card directly in VS Code.
        </p>
        <p className="mt-2 font-mono text-[10px] uppercase tracking-[0.08em] text-[var(--gray-500)]">
          drag by the handle · click a title to open
        </p>
      </header>

      <section className="mb-5 rounded-[10px] border-[1.5px] border-[var(--gray-200)] bg-white p-3 shadow-[0_1px_3px_rgba(20,20,19,0.04)]">
        <form
          className="flex flex-col gap-2 sm:flex-row sm:items-center"
          onSubmit={(event) => {
            event.preventDefault()
            void applyFolder()
          }}
        >
          <label htmlFor="task-folder" className="shrink-0 font-mono text-[10px] uppercase tracking-[0.08em] text-[var(--gray-500)]">
            Task folder
          </label>
          <input
            id="task-folder"
            value={folderInput}
            onChange={(event) => setFolderInput(event.target.value)}
            className="min-w-0 flex-1 rounded-md border-[1.5px] border-[var(--gray-200)] bg-[var(--ivory)] px-3 py-2 font-mono text-xs outline-none transition focus:border-[var(--clay)] focus:ring-2 focus:ring-[color-mix(in_srgb,var(--clay)_18%,transparent)]"
            placeholder="workspace/docs/kanban"
            spellCheck={false}
          />
          <button
            type="submit"
            className="rounded-md bg-[var(--slate)] px-4 py-2 text-xs font-semibold text-white transition hover:bg-[var(--gray-800)] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--clay)] disabled:opacity-50"
            disabled={isLoading}
          >
            Apply
          </button>
          <button
            type="button"
            className="rounded-md border-[1.5px] border-[var(--gray-200)] px-4 py-2 text-xs font-semibold transition hover:border-[var(--gray-500)] hover:bg-[var(--gray-50)] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--clay)] disabled:opacity-50"
            onClick={() => void refreshBoard()}
            disabled={!activeDirectory || isLoading}
          >
            Refresh
          </button>
        </form>
      </section>

      <div className="mb-4 flex min-h-6 flex-wrap items-center gap-x-3 gap-y-1 font-mono text-[10px] uppercase tracking-[0.06em] text-[var(--gray-500)]">
        {counts?.map((count) => <span key={count}>{count}</span>)}
        <span className="ml-auto flex items-center gap-1.5">
          <span
            className={`size-2 rounded-full ${watcherState === 'connected' ? 'bg-[var(--olive)]' : watcherState === 'connecting' ? 'bg-[var(--oat)]' : 'bg-[var(--clay)]'}`}
          />
          {watcherState === 'connected' ? 'Live' : watcherState === 'connecting' ? 'Connecting' : 'Watcher disconnected'}
        </span>
      </div>

      {error && (
        <div role="alert" className="mb-4 rounded-lg border border-[var(--clay)] bg-[var(--error-bg)] px-4 py-3 text-sm text-[var(--clay-d)]">
          {error}
        </div>
      )}

      {board && board.warnings.length > 0 && (
        <details className="mb-4 rounded-lg border border-[var(--oat)] bg-white px-4 py-3 text-xs">
          <summary className="cursor-pointer font-semibold">
            {board.warnings.length} task-file warning{board.warnings.length === 1 ? '' : 's'}
          </summary>
          <ul className="mt-2 space-y-1 font-mono text-[11px] text-[var(--gray-800)]">
            {board.warnings.map((warning, index) => (
              <li key={`${warning.path}-${index}`}>
                <strong>{warning.path}</strong>: {warning.message}
              </li>
            ))}
          </ul>
        </details>
      )}

      {isLoading && !board ? (
        <div className="grid min-h-80 place-items-center font-mono text-xs uppercase tracking-[0.08em] text-[var(--gray-500)]">
          Loading board…
        </div>
      ) : board && config ? (
        <div aria-busy={isMoving}>
        <KanbanBoard
          statuses={config.statuses}
          tasks={board.tasks}
          disabled={isMoving}
          onMove={(taskPath, status, index) => void handleMove(taskPath, status, index)}
          onOpen={(task) => void handleOpen(task)}
        />
        {isMoving && <p role="status" className="font-mono text-xs text-[var(--gray-500)]">Saving task move…</p>}
        </div>
      ) : (
        <div className="grid min-h-80 place-items-center rounded-lg border border-dashed border-[var(--gray-200)] text-sm text-[var(--gray-500)]">
          The board could not be loaded.
        </div>
      )}
    </main>
  )
}
