import { DragDropProvider, type DragEndEvent, type DragMoveEvent, type DragStartEvent } from '@dnd-kit/react'
import { isSortable } from '@dnd-kit/react/sortable'
import { useRef, useState } from 'react'
import type { KanbanStatus, TaskDto } from '../api'
import { tasksForStatus } from '../boardState'
import { KanbanColumn } from './KanbanColumn'
import { getDropDestination } from './dropDestination'

interface KanbanBoardProps {
  statuses: KanbanStatus[]
  tasks: TaskDto[]
  onMove: (taskPath: string, status: KanbanStatus, index: number) => void
  onOpen: (task: TaskDto) => void
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

export function KanbanBoard({ statuses, tasks, onMove, onOpen }: KanbanBoardProps) {
  const [highlightedColumn, setHighlightedColumn] = useState<KanbanStatus | null>(null)
  const originalCardPosition = useRef<OriginalCardPosition | null>(null)

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
    const nativeEvent = event.nativeEvent
    if (!(nativeEvent instanceof MouseEvent)) {
      return
    }

    const element = document.elementFromPoint(nativeEvent.clientX, nativeEvent.clientY)
    const status = element?.closest<HTMLElement>('[data-status]')?.dataset.status
    setHighlightedColumn(statuses.find((candidate) => candidate === status) ?? null)
  }

  const handleDragEnd = (event: DragEndEvent) => {
    setHighlightedColumn(null)

    const source = event.operation.source
    const target = event.operation.target
    const sourceData = source?.data as TaskDragData | undefined
    if (event.canceled || sourceData?.kind !== 'task' || !target || !isSortable(source)) {
      originalCardPosition.current = null
      return
    }

    const destination = getDropDestination(
      sourceData.taskPath,
      source.sortable.group,
      source.sortable.index,
      target.data,
      tasks,
      statuses,
    )
    if (!destination) {
      originalCardPosition.current = null
      return
    }

    const original = originalCardPosition.current
    originalCardPosition.current = null
    if (original) {
      const nextSibling = original.nextSibling?.parentNode === original.parent
        ? original.nextSibling
        : null
      original.parent.insertBefore(original.element, nextSibling)
    }

    onMove(sourceData.taskPath, destination.status, destination.index)
  }

  return (
    <DragDropProvider
      onDragStart={handleDragStart}
      onDragMove={handleDragMove}
      onDragEnd={handleDragEnd}
    >
      <div className="flex min-h-[28rem] gap-3 overflow-x-auto pb-4" aria-label="Kanban board">
        {statuses.map((status) => (
          <KanbanColumn
            key={status}
            status={status}
            tasks={tasksForStatus(tasks, status)}
            onOpen={onOpen}
            isHighlighted={highlightedColumn === status}
          />
        ))}
      </div>
    </DragDropProvider>
  )
}
