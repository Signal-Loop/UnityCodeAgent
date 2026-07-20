import { Type, type Static } from '@sinclair/typebox'

export const KANBAN_STATUSES = [
  'Backlog',
  'Started',
  'Planning',
  'Ready',
  'ToDo',
  'InProgress',
  'Completed',
] as const

export const KanbanStatusSchema = Type.Union([
  Type.Literal('Backlog'),
  Type.Literal('Started'),
  Type.Literal('Planning'),
  Type.Literal('Ready'),
  Type.Literal('ToDo'),
  Type.Literal('InProgress'),
  Type.Literal('Completed'),
])
export type KanbanStatus = Static<typeof KanbanStatusSchema>

export const TaskSchema = Type.Object({
  path: Type.String(),
  title: Type.String(),
  goal: Type.Union([Type.String(), Type.Null()]),
  status: KanbanStatusSchema,
  order: Type.Integer({ minimum: 1 }),
  version: Type.String(),
})
export type TaskDto = Static<typeof TaskSchema>

export const BoardWarningSchema = Type.Object({
  path: Type.String(),
  message: Type.String(),
})
export type BoardWarning = Static<typeof BoardWarningSchema>

export const BoardResponseSchema = Type.Object({
  directory: Type.String(),
  revision: Type.String(),
  tasks: Type.Array(TaskSchema),
  warnings: Type.Array(BoardWarningSchema),
})
export type BoardResponse = Static<typeof BoardResponseSchema>

export const ConfigResponseSchema = Type.Object({
  workspace_root: Type.String(),
  default_task_directory: Type.String(),
  statuses: Type.Array(KanbanStatusSchema),
})
export type ConfigResponse = Static<typeof ConfigResponseSchema>

export const DirectoryQuerySchema = Type.Object({ directory: Type.String({ minLength: 1 }) })

export const MoveTaskRequestSchema = Type.Object({
  directory: Type.String({ minLength: 1 }),
  task_path: Type.String({ minLength: 1 }),
  target_status: KanbanStatusSchema,
  target_index: Type.Integer({ minimum: 0 }),
  revision: Type.String(),
})
export type MoveTaskRequest = Static<typeof MoveTaskRequestSchema>

export const OpenTaskRequestSchema = Type.Object({
  directory: Type.String({ minLength: 1 }),
  task_path: Type.String({ minLength: 1 }),
})
export type OpenTaskRequest = Static<typeof OpenTaskRequestSchema>

export const ErrorResponseSchema = Type.Object({ detail: Type.String() })
