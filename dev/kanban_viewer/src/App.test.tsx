import { act, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { BoardResponse, ConfigResponse } from './api'
import App from './App'
import { eventsUrl, getBoard, getConfig } from './api'

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
    expect(FakeEventSource.instances[0].url).toBe(eventsUrl(config.default_task_directory))

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
})
