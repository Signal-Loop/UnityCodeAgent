import { useSortable } from '@dnd-kit/react/sortable'
import type { TaskDto } from '../api'

interface TaskCardProps {
  task: TaskDto
  index: number
  onOpen: (task: TaskDto) => void
}

export function TaskCard({ task, index, onOpen }: TaskCardProps) {
  const { ref, handleRef, isDragging } = useSortable({
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
      className={`group flex min-h-16 items-start gap-2 rounded-lg border-[1.5px] bg-white px-3 py-3 text-[13px] leading-[1.35] text-[var(--slate)] transition-[border-color,box-shadow,opacity] duration-150 hover:border-[var(--gray-500)] hover:shadow-[0_1px_4px_rgba(20,20,19,0.08)] ${isDragging ? 'opacity-35' : ''}`}
      data-testid={`task-${task.path}`}
    >
      <button
        ref={handleRef}
        type="button"
        className="mt-0.5 shrink-0 cursor-grab rounded p-1 text-[var(--gray-500)] outline-none hover:bg-[var(--gray-50)] hover:text-[var(--slate)] focus-visible:ring-2 focus-visible:ring-[var(--clay)] active:cursor-grabbing"
        aria-label={`Drag ${task.title}`}
        title="Drag to move or reorder"
      >
        <span className="grid grid-cols-2 gap-[2px]" aria-hidden="true">
          {Array.from({ length: 6 }, (_, dot) => (
            <span key={dot} className="size-[2px] rounded-full bg-current" />
          ))}
        </span>
      </button>
      <button
        type="button"
        className="min-w-0 flex-1 cursor-pointer text-left outline-none focus-visible:rounded focus-visible:ring-2 focus-visible:ring-[var(--clay)]"
        onClick={() => onOpen(task)}
        title={`Open ${task.path} in VS Code`}
      >
        {task.title}
      </button>
    </article>
  )
}
