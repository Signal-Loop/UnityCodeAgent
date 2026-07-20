import { useDroppable } from '@dnd-kit/react'
import type { KanbanStatus, TaskDto } from '../api'
import { TaskCard } from './TaskCard'

interface KanbanColumnProps {
  status: KanbanStatus
  tasks: TaskDto[]
  onOpen: (task: TaskDto) => void
  isHighlighted: boolean
  disabled: boolean
}

export function KanbanColumn({ status, tasks, onOpen, isHighlighted, disabled }: KanbanColumnProps) {
  const { ref } = useDroppable({
    id: `column:${status}`,
    type: 'column',
    accept: 'task',
    data: { kind: 'column', status },
  })

  return (
    <section
      ref={ref}
      className={`flex h-full min-h-48 w-72 shrink-0 flex-col overflow-hidden rounded-[10px] border-[1.5px] border-[var(--gray-200)] border-t-[3px] border-t-[var(--column-accent)] bg-white transition-[outline,background-color] ${isHighlighted ? 'bg-[var(--drop-bg)] outline-2 outline-offset-[-6px] outline-dashed outline-[var(--clay)]' : ''}`}
      data-status={status}
    >
      <header className="sticky top-0 z-10 flex items-baseline gap-2 border-b-[1.5px] border-[var(--gray-50)] bg-white px-3.5 py-3">
        <h2 className="font-serif text-[17px] font-medium tracking-[-0.01em]">{status}</h2>
        <span className="ml-auto min-w-7 rounded-full border-[1.5px] border-[var(--gray-200)] bg-[var(--gray-50)] px-2 py-0.5 text-center font-mono text-[11px] text-[var(--gray-500)]">
          {tasks.length}
        </span>
      </header>
      <div className="flex min-h-20 flex-1 flex-col gap-2 p-2.5">
        {tasks.map((task, index) => (
          <TaskCard key={task.path} task={task} index={index} onOpen={onOpen} disabled={disabled} />
        ))}
        {tasks.length === 0 && (
          <div className="grid min-h-20 place-items-center rounded-lg border border-dashed border-[var(--gray-200)] px-4 text-center font-mono text-[10px] uppercase tracking-[0.08em] text-[var(--gray-500)]">
            Drop a task here
          </div>
        )}
      </div>
    </section>
  )
}
