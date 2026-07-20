import type { KanbanStatus, TaskDto } from '../api'
import { tasksForStatus } from '../boardState'

interface ColumnTargetData {
  kind: 'column'
  status: KanbanStatus
}

export interface DropDestination {
  status: KanbanStatus
  index: number
}

export function getDropDestination(
  taskPath: string,
  sortableGroup: unknown,
  sortableIndex: number,
  targetData: unknown,
  tasks: TaskDto[],
  statuses: KanbanStatus[],
): DropDestination | null {
  const columnTarget = targetData as ColumnTargetData | undefined
  if (columnTarget?.kind === 'column') {
    return {
      status: columnTarget.status,
      index: tasksForStatus(tasks, columnTarget.status).filter((task) => task.path !== taskPath)
        .length,
    }
  }

  const status = statuses.find((candidate) => candidate === sortableGroup)
  if (!status) {
    return null
  }

  const maximumIndex = tasksForStatus(tasks, status).filter((task) => task.path !== taskPath).length
  return {
    status,
    index: Math.min(Math.max(sortableIndex, 0), maximumIndex),
  }
}
