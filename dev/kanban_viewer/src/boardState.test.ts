import { describe, expect, it } from 'vitest'
import type { KanbanStatus, TaskDto } from './api'
import { reorderTasks } from './boardState'

const statuses: KanbanStatus[] = [
  'Backlog',
  'Started',
  'Planning',
  'Ready',
  'ToDo',
  'InProgress',
  'Completed',
]

const tasks: TaskDto[] = [
  { path: 'a.md', title: 'A', status: 'Backlog', order: 100, version: 'a' },
  { path: 'b.md', title: 'B', status: 'Backlog', order: 200, version: 'b' },
  { path: 'c.md', title: 'C', status: 'Ready', order: 100, version: 'c' },
]

describe('reorderTasks', () => {
  it('reorders within a status using the final target index', () => {
    const result = reorderTasks(tasks, 'a.md', 'Backlog', 1, statuses)
    expect(result.filter((task) => task.status === 'Backlog').map((task) => task.path)).toEqual([
      'b.md',
      'a.md',
    ])
  })

  it('moves a task between statuses', () => {
    const result = reorderTasks(tasks, 'b.md', 'Ready', 0, statuses)
    expect(result.filter((task) => task.status === 'Ready').map((task) => task.path)).toEqual([
      'b.md',
      'c.md',
    ])
    expect(result.find((task) => task.path === 'b.md')?.status).toBe('Ready')
  })
})
