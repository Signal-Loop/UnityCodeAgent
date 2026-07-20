import type { KanbanStatus, TaskDto } from '../api'
import { tasksForStatus } from '../boardState'

interface ColumnTargetData {
  kind: 'column' | 'task'
  status: KanbanStatus
  taskPath?: string
}

export interface DropDestination {
  status: KanbanStatus
  index: number
}

export function getDropStatus(
  taskPath: string,
  sortableGroup: unknown,
  targetData: unknown,
  statuses: KanbanStatus[],
): KanbanStatus | null {
  const target = targetData as ColumnTargetData | undefined
  const targetStatus = statuses.find((status) => status === target?.status)
  const liveStatus = statuses.find((status) => status === sortableGroup)
  if (target?.kind === 'column') return targetStatus ?? null
  if (target?.kind === 'task' && target.taskPath === taskPath) return liveStatus ?? targetStatus ?? null
  return targetStatus ?? liveStatus ?? null
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
  const status = getDropStatus(taskPath, sortableGroup, targetData, statuses)
  if (!status) return null
  const maximumIndex = tasksForStatus(tasks, status).filter((task) => task.path !== taskPath).length
  if (columnTarget?.kind === 'column') {
    return {
      status,
      index: sortableGroup === status
        ? Math.min(Math.max(sortableIndex, 0), maximumIndex)
        : maximumIndex,
    }
  }

  return {
    status,
    index: Math.min(Math.max(sortableIndex, 0), maximumIndex),
  }
}
