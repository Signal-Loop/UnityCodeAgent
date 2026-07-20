import { DragDropProvider, type DragEndEvent, type DragMoveEvent, type DragStartEvent } from '@dnd-kit/react'
import { isSortable } from '@dnd-kit/react/sortable'
import { useCallback, useEffect, useRef, useState } from 'react'
import type { KanbanStatus, TaskDto } from '../api'
import { tasksForStatus } from '../boardState'
import { KanbanColumn } from './KanbanColumn'
import { getDropDestination, getDropStatus } from './dropDestination'

interface KanbanBoardProps {
  statuses: KanbanStatus[]
  tasks: TaskDto[]
  onMove: (taskPath: string, status: KanbanStatus, index: number) => void
  onOpen: (task: TaskDto) => void
  disabled?: boolean
}

interface TaskDragData {
  kind: 'task'
  taskPath: string
}

interface OriginalCardPosition {
  element: Element
  parent: Node
  nextSibling: ChildNode | null
}

export function KanbanBoard({ statuses, tasks, onMove, onOpen, disabled = false }: KanbanBoardProps) {
  const [highlightedColumn, setHighlightedColumn] = useState<KanbanStatus | null>(null)
  const originalCardPosition = useRef<OriginalCardPosition | null>(null)

  const restoreOriginalCard = useCallback(() => {
    const original = originalCardPosition.current
    originalCardPosition.current = null
    if (!original) return
    const nextSibling = original.nextSibling?.parentNode === original.parent ? original.nextSibling : null
    original.parent.insertBefore(original.element, nextSibling)
  }, [])

  useEffect(() => restoreOriginalCard, [restoreOriginalCard])

  const handleDragStart = (event: DragStartEvent) => {
    const source = event.operation.source
    if (!isSortable(source) || !source.sortable.element?.parentNode) {
      return
    }

    originalCardPosition.current = {
      element: source.sortable.element,
      parent: source.sortable.element.parentNode,
      nextSibling: source.sortable.element.nextSibling,
    }
  }

  const handleDragMove = (event: DragMoveEvent) => {
    const source = event.operation.source
    const sourceData = source?.data as TaskDragData | undefined
    const taskPath = sourceData?.kind === 'task' ? sourceData.taskPath : ''
    const sortableGroup = isSortable(source) ? source.sortable.group : undefined
    setHighlightedColumn(getDropStatus(taskPath, sortableGroup, event.operation.target?.data, statuses))
  }

  const handleDragEnd = (event: DragEndEvent) => {
    setHighlightedColumn(null)
    const source = event.operation.source
    const target = event.operation.target
    const sourceData = source?.data as TaskDragData | undefined
    const taskPath = sourceData?.kind === 'task' ? sourceData.taskPath : null
    const destination = !event.canceled && taskPath && target && isSortable(source)
      ? getDropDestination(
          taskPath,
          source.sortable.group,
          source.sortable.index,
          target.data,
          tasks,
          statuses,
        )
      : null

    restoreOriginalCard()
    if (!destination || !taskPath) {
      return
    }

    onMove(taskPath, destination.status, destination.index)
  }

  return (
    <DragDropProvider
      onDragStart={handleDragStart}
      onDragMove={handleDragMove}
      onDragEnd={handleDragEnd}
    >
      <p id="kanban-drag-instructions" className="sr-only">
        Use pointer, touch, or keyboard controls to move this task. Press Escape to cancel.
      </p>
      <div className="flex min-h-[28rem] gap-3 overflow-x-auto pb-4" aria-label="Kanban board">
        {statuses.map((status) => (
          <KanbanColumn
            key={status}
            status={status}
            tasks={tasksForStatus(tasks, status)}
            onOpen={onOpen}
            disabled={disabled}
            isHighlighted={highlightedColumn === status}
          />
        ))}
      </div>
    </DragDropProvider>
  )
}
