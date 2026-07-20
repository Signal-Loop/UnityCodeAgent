import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { KanbanStatus, TaskDto } from '../api'
import { KanbanBoard } from './KanbanBoard'
import { getDropDestination } from './dropDestination'

const statuses: KanbanStatus[] = [
  'Backlog',
  'Started',
  'Planning',
  'Ready',
  'ToDo',
  'InProgress',
  'Completed',
]

const task: TaskDto = {
  path: 'task.md',
  title: 'Only the task title',
  goal: 'This is the task goal.',
  status: 'Backlog',
  order: 100,
  version: 'version',
}

describe('KanbanBoard', () => {
  it('renders every workflow status, title, and goal', () => {
    render(<KanbanBoard statuses={statuses} tasks={[task]} onMove={vi.fn()} onOpen={vi.fn()} />)

    for (const status of statuses) {
      expect(screen.getByRole('heading', { name: status })).toBeInTheDocument()
    }
    expect(screen.getByRole('button', { name: 'Only the task title' })).toBeInTheDocument()
    expect(screen.getByText('This is the task goal.')).toBeInTheDocument()
    expect(screen.queryByText('task.md')).not.toBeInTheDocument()
  })

  it('opens a task from its title while the card is the drag target', () => {
    const onOpen = vi.fn()
    render(<KanbanBoard statuses={statuses} tasks={[task]} onMove={vi.fn()} onOpen={onOpen} />)

    fireEvent.click(screen.getByRole('button', { name: 'Only the task title' }))
    expect(onOpen).toHaveBeenCalledWith(task)
    const goal = screen.getByText('This is the task goal.')
    expect(goal).not.toContainElement(screen.getByRole('button', { name: 'Only the task title' }))
    fireEvent.click(goal)
    expect(onOpen).toHaveBeenCalledTimes(1)
    expect(screen.getByRole('button', { name: 'Drag Only the task title' })).toBeInTheDocument()
  })

  it('uses the sortable source position after reordering over a card', () => {
    const secondTask: TaskDto = { ...task, path: 'second.md', title: 'Second task', order: 200 }
    const destination = getDropDestination(
      task.path,
      'Backlog',
      1,
      { kind: 'task', taskPath: task.path, status: 'Backlog' },
      [task, secondTask],
      statuses,
    )

    expect(destination).toEqual({ status: 'Backlog', index: 1 })
  })

  it('uses the target card status for cross-column movement', () => {
    const destination = getDropDestination(
      task.path,
      'Backlog',
      0,
      { kind: 'task', taskPath: 'ready.md', status: 'Ready' },
      [task, { ...task, path: 'ready.md', status: 'Ready' }],
      statuses,
    )
    expect(destination).toEqual({ status: 'Ready', index: 0 })
  })

  it('appends when dropped directly on a column', () => {
    const destination = getDropDestination(
      task.path,
      'Backlog',
      0,
      { kind: 'column', status: 'Started' },
      [task],
      statuses,
    )

    expect(destination).toEqual({ status: 'Started', index: 0 })
  })

  it('uses the live insertion gap instead of appending when a populated column is the target', () => {
    const readyOne: TaskDto = { ...task, path: 'ready-one.md', status: 'Ready', order: 100 }
    const readyTwo: TaskDto = { ...task, path: 'ready-two.md', status: 'Ready', order: 200 }
    const destination = getDropDestination(
      task.path,
      'Ready',
      1,
      { kind: 'column', status: 'Ready' },
      [task, readyOne, readyTwo],
      statuses,
    )

    expect(destination).toEqual({ status: 'Ready', index: 1 })
  })

  it('rejects unknown targets and clamps sortable indexes', () => {
    expect(getDropDestination(task.path, 'Unknown', 0, {}, [task], statuses)).toBeNull()
    expect(getDropDestination(task.path, 'Backlog', -10, { kind: 'task', status: 'Backlog' }, [task], statuses)).toEqual({ status: 'Backlog', index: 0 })
  })
})
