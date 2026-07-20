import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { BoardResponse, ConfigResponse } from './api'
import App from './App'
import { eventsUrl, getBoard, getConfig, moveTask, openTask } from './api'

vi.mock('./api', async (importOriginal) => {
  const original = await importOriginal<typeof import('./api')>()
  return {
    ...original,
    getConfig: vi.fn(),
    getBoard: vi.fn(),
    moveTask: vi.fn(),
    openTask: vi.fn(),
  }
})

vi.mock('./components/KanbanBoard', () => ({
  KanbanBoard: ({ tasks, onMove, onOpen, disabled }: {
    tasks: BoardResponse['tasks']
    onMove: (path: string, status: 'Ready', index: number) => void
    onOpen: (task: BoardResponse['tasks'][number]) => void
    disabled: boolean
  }) => <div>
    {tasks.map((task) => <button key={task.path} onClick={() => onOpen(task)}>{task.title}</button>)}
    {tasks[0] && <button disabled={disabled} onClick={() => onMove(tasks[0].path, 'Ready', 0)}>Move first</button>}
  </div>,
}))

const config: ConfigResponse = {
  workspace_root: 'C:\\workspace',
  default_task_directory: 'C:\\workspace\\docs\\kanban',
  statuses: ['Backlog', 'Started', 'Planning', 'Ready', 'ToDo', 'InProgress', 'Completed'],
}

const initialBoard: BoardResponse = {
  directory: config.default_task_directory,
  revision: 'one',
  tasks: [
    {
      path: 'one.md',
      title: 'Initial task',
      goal: null,
      status: 'Backlog',
      order: 100,
      version: 'one',
    },
  ],
  warnings: [],
}

class FakeEventSource {
  static instances: FakeEventSource[] = []
  onopen: ((event: Event) => void) | null = null
  onerror: ((event: Event) => void) | null = null
  readonly url: string
  private listeners = new Map<string, Array<(event: Event) => void>>()

  constructor(url: string | URL) {
    this.url = String(url)
    FakeEventSource.instances.push(this)
  }

  addEventListener(name: string, listener: EventListenerOrEventListenerObject) {
    const callback =
      typeof listener === 'function' ? listener : (event: Event) => listener.handleEvent(event)
    this.listeners.set(name, [...(this.listeners.get(name) ?? []), callback])
  }

  emit(name: string) {
    for (const listener of this.listeners.get(name) ?? []) {
      listener(new Event(name))
    }
  }

  close() {}
}

const getConfigMock = vi.mocked(getConfig)
const getBoardMock = vi.mocked(getBoard)
const moveTaskMock = vi.mocked(moveTask)
const openTaskMock = vi.mocked(openTask)

beforeEach(() => {
  localStorage.clear()
  FakeEventSource.instances = []
  vi.clearAllMocks()
  vi.stubGlobal('EventSource', FakeEventSource)
  getConfigMock.mockResolvedValue(config)
  getBoardMock.mockResolvedValue(initialBoard)
})

describe('App', () => {
  it('loads and remembers the default board, then reports a live watcher', async () => {
    render(<App />)

    expect(await screen.findByRole('button', { name: 'Initial task' })).toBeInTheDocument()
    expect(screen.getByLabelText('Task folder')).toHaveValue(config.default_task_directory)
    expect(localStorage.getItem('kanban-viewer.task-directory')).toBe(
      config.default_task_directory,
    )
    await waitFor(() => expect(FakeEventSource.instances[0]?.url).toBe(eventsUrl(config.default_task_directory)))

    act(() => FakeEventSource.instances[0].onopen?.(new Event('open')))
    expect(screen.getByText('Live')).toBeInTheDocument()
  })

  it('refreshes the board when a Markdown change event arrives', async () => {
    const changedBoard: BoardResponse = {
      ...initialBoard,
      revision: 'two',
      tasks: [
        ...initialBoard.tasks,
        {
          path: 'two.md',
          title: 'Task from watcher',
          goal: null,
          status: 'Planning',
          order: 100,
          version: 'two',
        },
      ],
    }
    getBoardMock.mockResolvedValueOnce(initialBoard).mockResolvedValueOnce(changedBoard)
    render(<App />)
    expect(await screen.findByRole('button', { name: 'Initial task' })).toBeInTheDocument()

    act(() => FakeEventSource.instances[0].emit('board-changed'))

    expect(await screen.findByRole('button', { name: 'Task from watcher' })).toBeInTheDocument()
    await waitFor(() => expect(getBoardMock).toHaveBeenCalledTimes(2))
  })

  it('applies and remembers a new folder and supports manual refresh', async () => {
    const other = { ...initialBoard, directory: 'C:\\other', revision: 'other' }
    getBoardMock.mockResolvedValueOnce(initialBoard).mockResolvedValueOnce(other).mockResolvedValueOnce(other)
    render(<App />)
    await screen.findByRole('button', { name: 'Initial task' })
    fireEvent.change(screen.getByLabelText('Task folder'), { target: { value: other.directory } })
    fireEvent.click(screen.getByRole('button', { name: 'Apply' }))
    await waitFor(() => expect(localStorage.getItem('kanban-viewer.task-directory')).toBe(other.directory))
    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }))
    await waitFor(() => expect(getBoardMock).toHaveBeenCalledTimes(3))
  })

  it('falls back from an invalid saved directory to the default', async () => {
    localStorage.setItem('kanban-viewer.task-directory', 'missing')
    getBoardMock.mockRejectedValueOnce(new Error('missing')).mockResolvedValueOnce(initialBoard)
    render(<App />)
    expect(await screen.findByRole('button', { name: 'Initial task' })).toBeInTheDocument()
    expect(getBoardMock).toHaveBeenNthCalledWith(1, 'missing', expect.any(AbortSignal))
    expect(localStorage.getItem('kanban-viewer.task-directory')).toBe(config.default_task_directory)
  })

  it('moves optimistically, adopts the response, and opens tasks', async () => {
    const moved = { ...initialBoard, revision: 'moved', tasks: [{ ...initialBoard.tasks[0], status: 'Ready' as const }] }
    moveTaskMock.mockResolvedValue(moved)
    openTaskMock.mockResolvedValue()
    render(<App />)
    await screen.findByRole('button', { name: 'Initial task' })
    fireEvent.click(screen.getByRole('button', { name: 'Move first' }))
    await waitFor(() => expect(moveTaskMock).toHaveBeenCalledWith(expect.objectContaining({ task_path: 'one.md', target_status: 'Ready' })))
    fireEvent.click(screen.getByRole('button', { name: 'Initial task' }))
    await waitFor(() => expect(openTaskMock).toHaveBeenCalledWith(config.default_task_directory, 'one.md'))
  })

  it('refreshes authoritatively after a failed move and keeps the mutation error', async () => {
    moveTaskMock.mockRejectedValue(new Error('Move failed'))
    getBoardMock.mockResolvedValueOnce(initialBoard).mockResolvedValueOnce(initialBoard)
    render(<App />)
    await screen.findByRole('button', { name: 'Initial task' })
    fireEvent.click(screen.getByRole('button', { name: 'Move first' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Move failed')
    await waitFor(() => expect(getBoardMock).toHaveBeenCalledTimes(2))
  })

  it('validates empty folders and reports initialization failures', async () => {
    getConfigMock.mockRejectedValueOnce(new Error('Service offline'))
    const { unmount } = render(<App />)
    expect(await screen.findByRole('alert')).toHaveTextContent('Service offline')
    unmount()
    getConfigMock.mockResolvedValue(config)
    getBoardMock.mockResolvedValue(initialBoard)
    render(<App />)
    await screen.findByRole('button', { name: 'Initial task' })
    fireEvent.change(screen.getByLabelText('Task folder'), { target: { value: '   ' } })
    fireEvent.click(screen.getByRole('button', { name: 'Apply' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Enter a task folder path.')
  })

  it('reports watcher, refresh, folder, and editor errors', async () => {
    render(<App />)
    await screen.findByRole('button', { name: 'Initial task' })
    await waitFor(() => expect(FakeEventSource.instances[0]).toBeDefined())
    act(() => FakeEventSource.instances[0].onerror?.(new Event('error')))
    expect(screen.getByText('Watcher disconnected')).toBeInTheDocument()

    getBoardMock.mockRejectedValueOnce(new Error('Refresh failed'))
    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Refresh failed')

    getBoardMock.mockRejectedValueOnce(new Error('Folder failed'))
    fireEvent.change(screen.getByLabelText('Task folder'), { target: { value: 'bad-folder' } })
    fireEvent.click(screen.getByRole('button', { name: 'Apply' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Folder failed')

    openTaskMock.mockRejectedValueOnce(new Error('Open failed'))
    fireEvent.click(screen.getByRole('button', { name: 'Initial task' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Open failed')
  })

  it('coalesces a watcher event received during a move', async () => {
    let resolveMove!: (board: BoardResponse) => void
    moveTaskMock.mockImplementationOnce(() => new Promise((resolve) => { resolveMove = resolve }))
    getBoardMock.mockResolvedValue(initialBoard)
    render(<App />)
    await screen.findByRole('button', { name: 'Initial task' })
    await waitFor(() => expect(FakeEventSource.instances[0]).toBeDefined())
    fireEvent.click(screen.getByRole('button', { name: 'Move first' }))
    expect(await screen.findByRole('status')).toHaveTextContent('Saving task move')
    act(() => FakeEventSource.instances[0].emit('board-changed'))
    await act(async () => resolveMove({ ...initialBoard, revision: 'moved' }))
    await waitFor(() => expect(getBoardMock).toHaveBeenCalledTimes(2))
  })

  it('coalesces watcher bursts received during an active refresh', async () => {
    let resolveRefresh!: (board: BoardResponse) => void
    getBoardMock
      .mockResolvedValueOnce(initialBoard)
      .mockImplementationOnce(() => new Promise((resolve) => { resolveRefresh = resolve }))
      .mockResolvedValueOnce({ ...initialBoard, revision: 'follow-up' })
    render(<App />)
    await screen.findByRole('button', { name: 'Initial task' })
    await waitFor(() => expect(FakeEventSource.instances[0]).toBeDefined())
    act(() => FakeEventSource.instances[0].emit('board-changed'))
    act(() => FakeEventSource.instances[0].emit('board-changed'))
    await act(async () => resolveRefresh({ ...initialBoard, revision: 'refresh' }))
    await waitFor(() => expect(getBoardMock).toHaveBeenCalledTimes(3))
  })

  it('restores the snapshot when a move and authoritative refresh both fail', async () => {
    moveTaskMock.mockRejectedValueOnce(new Error('Move failed'))
    getBoardMock.mockResolvedValueOnce(initialBoard).mockRejectedValueOnce(new Error('Refresh failed'))
    render(<App />)
    await screen.findByRole('button', { name: 'Initial task' })
    fireEvent.click(screen.getByRole('button', { name: 'Move first' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Move failed')
    expect(screen.getByRole('button', { name: 'Initial task' })).toBeInTheDocument()
  })

  it('renders singular and plural task warnings', async () => {
    getBoardMock.mockResolvedValueOnce({ ...initialBoard, warnings: [{ path: 'one.md', message: 'One warning' }] })
    const view = render(<App />)
    expect(await screen.findByText('1 task-file warning')).toBeInTheDocument()
    view.unmount()
    getBoardMock.mockResolvedValueOnce({ ...initialBoard, warnings: [
      { path: 'one.md', message: 'One warning' },
      { path: 'two.md', message: 'Two warning' },
    ] })
    render(<App />)
    expect(await screen.findByText('2 task-file warnings')).toBeInTheDocument()
  })

  it('uses a safe message for non-Error failures', async () => {
    getConfigMock.mockRejectedValueOnce('offline')
    render(<App />)
    expect(await screen.findByRole('alert')).toHaveTextContent('An unexpected error occurred.')
  })
})
