export type KanbanStatus =
  | 'Backlog'
  | 'Started'
  | 'Planning'
  | 'Ready'
  | 'ToDo'
  | 'InProgress'
  | 'Completed'

export interface TaskDto {
  path: string
  title: string
  goal: string | null
  status: KanbanStatus
  order: number
  version: string
}

export interface BoardWarning {
  path: string
  message: string
}

export interface BoardResponse {
  directory: string
  revision: string
  tasks: TaskDto[]
  warnings: BoardWarning[]
}

export interface ConfigResponse {
  workspace_root: string
  default_task_directory: string
  statuses: KanbanStatus[]
}

export interface MoveTaskRequest {
  directory: string
  task_path: string
  target_status: KanbanStatus
  target_index: number
  revision: string
}

interface ApiErrorBody {
  detail?: string
}

export class ApiError extends Error {
  readonly status: number

  constructor(message: string, status: number) {
    super(message)
    this.name = 'ApiError'
    this.status = status
  }
}

async function request<T>(input: RequestInfo | URL, init?: RequestInit): Promise<T> {
  const response = await fetch(input, init)
  if (!response.ok) {
    let message = `Request failed with status ${response.status}.`
    try {
      const body = (await response.json()) as ApiErrorBody
      if (body.detail) {
        message = body.detail
      }
    } catch {
      // Keep the status-based fallback for non-JSON errors.
    }
    throw new ApiError(message, response.status)
  }

  if (response.status === 204) {
    return undefined as T
  }
  return (await response.json()) as T
}

export function getConfig(): Promise<ConfigResponse> {
  return request<ConfigResponse>('/api/config')
}

export function getBoard(directory: string): Promise<BoardResponse> {
  const params = new URLSearchParams({ directory })
  return request<BoardResponse>(`/api/board?${params}`)
}

export function moveTask(move: MoveTaskRequest): Promise<BoardResponse> {
  return request<BoardResponse>('/api/board/tasks', {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(move),
  })
}

export function openTask(directory: string, taskPath: string): Promise<void> {
  return request<void>('/api/editor/open', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ directory, task_path: taskPath }),
  })
}

export function eventsUrl(directory: string): string {
  const params = new URLSearchParams({ directory })
  return `/api/events?${params}`
}
