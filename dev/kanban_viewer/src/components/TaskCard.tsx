import { useSortable } from '@dnd-kit/react/sortable'
import type { TaskDto } from '../api'

interface TaskCardProps {
  task: TaskDto
  index: number
  onOpen: (task: TaskDto) => void
}

export function TaskCard({ task, index, onOpen }: TaskCardProps) {
  const { ref, isDragging } = useSortable({
    id: task.path,
    index,
    group: task.status,
    type: 'task',
    accept: 'task',
    data: {
      kind: 'task',
      taskPath: task.path,
      status: task.status,
    },
  })

  return (
    <article
      ref={ref}
      className={`group flex min-h-16 flex-col cursor-grab items-start rounded-lg border-[1.5px] bg-white px-3 py-3 text-[13px] leading-[1.35] text-[var(--slate)] transition-[border-color,box-shadow,opacity] duration-150 hover:border-[var(--gray-500)] hover:shadow-[0_1px_4px_rgba(20,20,19,0.08)] active:cursor-grabbing ${isDragging ? 'opacity-35' : ''}`}
      data-testid={`task-${task.path}`}
    >
      <button
        type="button"
        className="min-w-0 cursor-pointer text-left outline-none focus-visible:rounded focus-visible:ring-2 focus-visible:ring-[var(--clay)]"
        onClick={() => onOpen(task)}
        aria-label={task.title}
        title={`Open ${task.path} in VS Code`}
      >
        <span className="font-bold">{task.title}</span>
      </button>
      {task.goal && <span className="mt-1 block text-[var(--gray-800)]">{task.goal}</span>}
    </article>
  )
}
