import { render } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { TaskDto } from '../api'
import { TaskCard } from './TaskCard'

const dnd = vi.hoisted(() => ({
  useSortable: vi.fn(),
}))

vi.mock('@dnd-kit/react/sortable', () => ({ useSortable: dnd.useSortable }))

const task: TaskDto = {
  path: 'task.md',
  title: 'Task',
  goal: null,
  status: 'Backlog',
  order: 100,
  version: 'version',
}

describe('TaskCard', () => {
  beforeEach(() => {
    dnd.useSortable.mockReturnValue({ ref: vi.fn(), isDragging: false })
  })

  it('registers as one sortable task in its status group', () => {
    render(<TaskCard task={task} index={0} onOpen={vi.fn()} />)

    expect(dnd.useSortable).toHaveBeenCalledWith(
      expect.objectContaining({
        id: task.path,
        index: 0,
        group: task.status,
        type: 'task',
        accept: 'task',
      }),
    )
  })

  it('keeps the task path and status in its drag data', () => {
    render(<TaskCard task={task} index={0} onOpen={vi.fn()} />)

    expect(dnd.useSortable).toHaveBeenCalledWith(
      expect.objectContaining({ data: expect.objectContaining({ taskPath: task.path }) }),
    )
  })
})
