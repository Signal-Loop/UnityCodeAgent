export type {
  BoardResponse,
  BoardWarning,
  ConfigResponse,
  KanbanStatus,
  MoveTaskRequest,
  TaskDto,
} from '../shared/contracts'
import type { BoardResponse, ConfigResponse, MoveTaskRequest } from '../shared/contracts'

interface ApiErrorBody { detail?: string }

export class ApiError extends Error {
  constructor(message: string, readonly status: number) {
    super(message)
    this.name = 'ApiError'
  }
}

async function request<T>(input: RequestInfo | URL, init?: RequestInit): Promise<T> {
  const response = await fetch(input, init)
  if (!response.ok) {
    let message = `Request failed with status ${response.status}.`
    try {
      const body = (await response.json()) as ApiErrorBody
      if (body.detail) message = body.detail
    } catch { /* retain the status fallback */ }
    throw new ApiError(message, response.status)
  }
  return response.status === 204 ? undefined as T : await response.json() as T
}

export function getConfig(signal?: AbortSignal): Promise<ConfigResponse> {
  return request<ConfigResponse>('/api/config', { signal })
}

export function getBoard(directory: string, signal?: AbortSignal): Promise<BoardResponse> {
  return request<BoardResponse>(`/api/board?${new URLSearchParams({ directory })}`, { signal })
}

export function moveTask(move: MoveTaskRequest, signal?: AbortSignal): Promise<BoardResponse> {
  return request<BoardResponse>('/api/board/tasks', {
    method: 'PATCH', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(move), signal,
  })
}

export function openTask(directory: string, taskPath: string, signal?: AbortSignal): Promise<void> {
  return request<void>('/api/editor/open', {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ directory, task_path: taskPath }), signal,
  })
}

export function eventsUrl(directory: string): string {
  return `/api/events?${new URLSearchParams({ directory })}`
}
