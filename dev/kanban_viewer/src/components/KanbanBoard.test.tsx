import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { KanbanStatus, TaskDto } from '../api'
import { KanbanBoard } from './KanbanBoard'

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
    expect(goal).not.toContainElement(screen.getByRole('button'))
    fireEvent.click(goal)
    expect(onOpen).toHaveBeenCalledTimes(1)
    expect(screen.queryByRole('button', { name: 'Drag Only the task title' })).not.toBeInTheDocument()
    expect(screen.getByTestId('task-task.md')).toHaveClass('cursor-grab')
  })
})
