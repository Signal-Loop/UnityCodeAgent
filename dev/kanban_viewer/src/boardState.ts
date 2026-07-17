import type { KanbanStatus, TaskDto } from './api'

export function tasksForStatus(tasks: TaskDto[], status: KanbanStatus): TaskDto[] {
  return tasks.filter((task) => task.status === status)
}

export function reorderTasks(
  tasks: TaskDto[],
  taskPath: string,
  targetStatus: KanbanStatus,
  targetIndex: number,
  statuses: KanbanStatus[],
): TaskDto[] {
  const selected = tasks.find((task) => task.path === taskPath)
  if (!selected) {
    return tasks
  }

  const withoutSelected = tasks.filter((task) => task.path !== taskPath)
  const targetTasks = withoutSelected.filter((task) => task.status === targetStatus)
  const insertionIndex = Math.min(Math.max(targetIndex, 0), targetTasks.length)
  targetTasks.splice(insertionIndex, 0, { ...selected, status: targetStatus })

  return statuses.flatMap((status) =>
    status === targetStatus
      ? targetTasks
      : withoutSelected.filter((task) => task.status === status),
  )
}
