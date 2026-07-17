import { DragDropProvider, type DragEndEvent } from '@dnd-kit/react'
import { isSortable } from '@dnd-kit/react/sortable'
import type { KanbanStatus, TaskDto } from '../api'
import { tasksForStatus } from '../boardState'
import { KanbanColumn } from './KanbanColumn'

interface KanbanBoardProps {
  statuses: KanbanStatus[]
  tasks: TaskDto[]
  onMove: (taskPath: string, status: KanbanStatus, index: number) => void
  onOpen: (task: TaskDto) => void
}

interface TaskDragData {
  kind: 'task'
  taskPath: string
  status: KanbanStatus
}

interface ColumnDragData {
  kind: 'column'
  status: KanbanStatus
}

export function KanbanBoard({ statuses, tasks, onMove, onOpen }: KanbanBoardProps) {
  const handleDragEnd = (event: DragEndEvent) => {
    if (event.canceled || !event.operation.source || !event.operation.target) {
      return
    }

    const source = event.operation.source
    const target = event.operation.target
    const sourceData = source.data as TaskDragData | undefined
    const targetData = target.data as TaskDragData | ColumnDragData | undefined
    if (sourceData?.kind !== 'task' || !targetData) {
      return
    }

    const targetStatus = targetData.status
    const targetColumnTasks = tasksForStatus(tasks, targetStatus).filter(
      (task) => task.path !== sourceData.taskPath,
    )

    let targetIndex = targetColumnTasks.length
    if (targetData.kind === 'task') {
      const matchedIndex = targetColumnTasks.findIndex(
        (task) => task.path === targetData.taskPath,
      )
      if (matchedIndex >= 0) {
        targetIndex = matchedIndex
        if (isSortable(target) && target.sortable.index > matchedIndex) {
          targetIndex = Math.min(target.sortable.index, targetColumnTasks.length)
        }
      }
    }

    onMove(sourceData.taskPath, targetStatus, targetIndex)
  }

  return (
    <DragDropProvider onDragEnd={handleDragEnd}>
      <div className="flex min-h-[28rem] gap-3 overflow-x-auto pb-4" aria-label="Kanban board">
        {statuses.map((status) => (
          <KanbanColumn
            key={status}
            status={status}
            tasks={tasksForStatus(tasks, status)}
            onOpen={onOpen}
          />
        ))}
      </div>
    </DragDropProvider>
  )
}
