import { act, render, screen } from '@testing-library/react'
import type { ReactNode } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { TaskDto } from '../api'

const dnd = vi.hoisted(() => ({ props: {} as Record<string, (event: unknown) => void> }))

vi.mock('@dnd-kit/react', () => ({
  DragDropProvider: ({ children, ...props }: { children: ReactNode }) => {
    dnd.props = props
    return <>{children}</>
  },
}))
vi.mock('@dnd-kit/react/sortable', () => ({ isSortable: (value: unknown) => Boolean((value as { sortable?: unknown })?.sortable) }))
vi.mock('./KanbanColumn', () => ({
  KanbanColumn: ({ status, isHighlighted }: { status: string; isHighlighted: boolean }) => <div data-testid={status}>{isHighlighted ? 'highlighted' : ''}</div>,
}))

import { KanbanBoard } from './KanbanBoard'

const task: TaskDto = { path: 'task.md', title: 'Task', goal: null, status: 'Backlog', order: 100, version: 'v' }
const statuses = ['Backlog', 'Ready'] as const

function operation(element: Element, canceled = false) {
  return {
    canceled,
    operation: {
      source: { data: { kind: 'task', taskPath: task.path }, sortable: { element, group: 'Backlog', index: 0 } },
      target: { data: { kind: 'column', status: 'Ready' } },
    },
  }
}

function liveOperation(
  element: Element,
  originalParent: Element,
  targetParent: Element,
  targetStatus: 'Backlog' | 'Ready',
) {
  return {
    canceled: false,
    operation: {
      source: {
        data: { kind: 'task', taskPath: task.path },
        sortable: {
          element,
          get group() { return element.parentNode === targetParent ? targetStatus : 'Backlog' },
          get index() {
            const parent = element.parentNode === targetParent ? targetParent : originalParent
            return [...parent.children].indexOf(element)
          },
        },
      },
      target: { data: { kind: 'task', taskPath: 'target.md', status: targetStatus } },
    },
  }
}

describe('KanbanBoard drag session', () => {
  beforeEach(() => { dnd.props = {} })

  it('highlights typed targets, restores dnd DOM, and reports a valid move', () => {
    const onMove = vi.fn()
    render(<KanbanBoard statuses={[...statuses]} tasks={[task]} onMove={onMove} onOpen={vi.fn()} />)
    const original = document.createElement('div')
    const card = document.createElement('article')
    const sibling = document.createElement('span')
    original.append(card, sibling)
    const displaced = document.createElement('div')
    act(() => dnd.props.onDragStart(operation(card)))
    displaced.append(card)
    act(() => dnd.props.onDragMove(operation(card)))
    expect(screen.getByTestId('Ready')).toHaveTextContent('highlighted')
    act(() => dnd.props.onDragEnd(operation(card)))
    expect(original.firstChild).toBe(card)
    expect(onMove).toHaveBeenCalledWith('task.md', 'Ready', 0)
    expect(screen.getByTestId('Ready')).toBeEmptyDOMElement()
  })

  it('restores the original card on cancellation and unmount', () => {
    const onMove = vi.fn()
    const view = render(<KanbanBoard statuses={[...statuses]} tasks={[task]} onMove={onMove} onOpen={vi.fn()} />)
    const original = document.createElement('div')
    const card = document.createElement('article')
    original.append(card)
    const displaced = document.createElement('div')
    act(() => dnd.props.onDragStart(operation(card)))
    displaced.append(card)
    act(() => dnd.props.onDragEnd(operation(card, true)))
    expect(original.firstChild).toBe(card)
    expect(onMove).not.toHaveBeenCalled()

    act(() => dnd.props.onDragStart(operation(card)))
    displaced.append(card)
    view.unmount()
    expect(original.firstChild).toBe(card)
  })

  it('ignores invalid sources and targets', () => {
    const onMove = vi.fn()
    render(<KanbanBoard statuses={[...statuses]} tasks={[task]} onMove={onMove} onOpen={vi.fn()} />)
    act(() => dnd.props.onDragStart({ operation: { source: {} } }))
    act(() => dnd.props.onDragEnd({ canceled: false, operation: { source: { data: { kind: 'other' } }, target: null } }))
    const element = document.createElement('article')
    const parent = document.createElement('div')
    parent.append(element)
    act(() => dnd.props.onDragEnd({ canceled: false, operation: {
      source: { data: { kind: 'task', taskPath: task.path }, sortable: { element, group: 'Unknown', index: 0 } },
      target: { data: { kind: 'task', status: 'Unknown' } },
    } }))
    act(() => dnd.props.onDragMove({ operation: { target: { data: { status: 'Unknown' } } } }))
    expect(onMove).not.toHaveBeenCalled()
  })

  it('captures the live index before restoring a cross-column card over existing cards', () => {
    const onMove = vi.fn()
    const existingReady: TaskDto = { ...task, path: 'ready.md', status: 'Ready' }
    render(<KanbanBoard statuses={[...statuses]} tasks={[task, existingReady]} onMove={onMove} onOpen={vi.fn()} />)
    const original = document.createElement('div')
    const card = document.createElement('article')
    original.append(card)
    const target = document.createElement('div')
    target.append(document.createElement('article'))
    const event = liveOperation(card, original, target, 'Ready')
    act(() => dnd.props.onDragStart(event))
    target.append(card)
    act(() => dnd.props.onDragEnd(event))
    expect(original.firstChild).toBe(card)
    expect(onMove).toHaveBeenCalledWith('task.md', 'Ready', 1)
  })

  it('captures the live index before restoring a same-column reorder', () => {
    const onMove = vi.fn()
    const second: TaskDto = { ...task, path: 'second.md', order: 200 }
    const third: TaskDto = { ...task, path: 'third.md', order: 300 }
    render(<KanbanBoard statuses={[...statuses]} tasks={[task, second, third]} onMove={onMove} onOpen={vi.fn()} />)
    const column = document.createElement('div')
    const card = document.createElement('article')
    const secondCard = document.createElement('article')
    const thirdCard = document.createElement('article')
    column.append(card, secondCard, thirdCard)
    const event = liveOperation(card, column, column, 'Backlog')
    act(() => dnd.props.onDragStart(event))
    column.insertBefore(card, thirdCard)
    act(() => dnd.props.onDragEnd(event))
    expect(column.firstChild).toBe(card)
    expect(onMove).toHaveBeenCalledWith('task.md', 'Backlog', 1)
  })

  it('recognizes the dragged-card gap as the live target column', () => {
    const onMove = vi.fn()
    const existingReady: TaskDto = { ...task, path: 'ready.md', status: 'Ready' }
    render(<KanbanBoard statuses={[...statuses]} tasks={[task, existingReady]} onMove={onMove} onOpen={vi.fn()} />)
    const original = document.createElement('div')
    const card = document.createElement('article')
    original.append(card)
    const target = document.createElement('div')
    target.append(document.createElement('article'), card)
    const event = liveOperation(card, original, target, 'Ready')
    event.operation.target.data = { kind: 'task', taskPath: task.path, status: 'Backlog' }

    act(() => dnd.props.onDragStart(event))
    act(() => dnd.props.onDragMove(event))
    expect(screen.getByTestId('Ready')).toHaveTextContent('highlighted')
    expect(screen.getByTestId('Backlog')).toBeEmptyDOMElement()
    act(() => dnd.props.onDragEnd(event))
    expect(onMove).toHaveBeenCalledWith('task.md', 'Ready', 1)
  })
})
