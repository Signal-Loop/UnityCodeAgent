import { createHash, randomUUID } from 'node:crypto'
import { chmod, mkdir, open, readdir, readFile, realpath, rename, stat, unlink } from 'node:fs/promises'
import { dirname, isAbsolute, join, relative, resolve, sep } from 'node:path'
import type { BoardResponse, BoardWarning, MoveTaskRequest, TaskDto } from '../shared/contracts.js'
import { KANBAN_STATUSES } from '../shared/contracts.js'
import { parseTask, renderProperties, versionOf } from './markdown.js'

export class InvalidDirectoryError extends Error {}
export class InvalidTaskError extends Error {}
export class StaleBoardError extends Error {}
export class StorageError extends Error {}

export interface FileOperations {
  replace(source: string, target: string): Promise<void>
}

const defaultFileOperations: FileOperations = { replace: rename }

interface Change {
  path: string
  before: Buffer
  after: Buffer
  mode: number
  temporary?: string
}

export class KanbanRepository {
  readonly workspaceRoot: string
  private queue: Promise<void> = Promise.resolve()

  constructor(workspaceRoot: string, private readonly files: FileOperations = defaultFileOperations) {
    this.workspaceRoot = resolve(workspaceRoot)
  }

  async resolveDirectory(directory: string): Promise<string> {
    const candidate = isAbsolute(directory) ? directory : join(this.workspaceRoot, directory)
    try {
      const canonical = await realpath(candidate)
      if (!(await stat(canonical)).isDirectory()) throw new Error('not directory')
      return canonical
    } catch {
      throw new InvalidDirectoryError(`Task folder does not exist or is not a directory: ${candidate}`)
    }
  }

  async resolveTaskPath(directory: string, taskPath: string): Promise<string> {
    const normalized = taskPath.replaceAll('/', sep)
    if (isAbsolute(normalized) || normalized.split(sep).includes('..') || !normalized.toLowerCase().endsWith('.md')) {
      throw new InvalidTaskError('Task path must be a relative Markdown file path.')
    }
    try {
      const target = await realpath(join(directory, normalized))
      if (!inside(directory, target) || !(await stat(target)).isFile()) throw new Error('outside')
      return target
    } catch {
      throw new InvalidTaskError('Task path must stay inside the selected task folder.')
    }
  }

  loadBoard(directory: string): Promise<BoardResponse> {
    return this.serialized(() => this.loadBoardUnlocked(directory))
  }

  moveTask(request: MoveTaskRequest): Promise<BoardResponse> {
    return this.serialized(async () => {
      const snapshot = await this.loadBoardUnlocked(request.directory)
      if (snapshot.revision !== request.revision) throw new StaleBoardError('The board changed on disk. Refresh and try the move again.')
      const selected = snapshot.tasks.find((task) => task.path === request.task_path)
      if (!selected) throw new InvalidTaskError(`Task is not part of the current board: ${request.task_path}`)
      const targetTasks = snapshot.tasks.filter((task) => task.status === request.target_status && task.path !== selected.path)
      if (request.target_index > targetTasks.length) throw new InvalidTaskError(`Target index ${request.target_index} is outside the ${request.target_status} column.`)
      const reordered = [...targetTasks]
      reordered.splice(request.target_index, 0, selected)
      const original = snapshot.tasks.filter((task) => task.status === request.target_status).map((task) => task.path)
      if (selected.status === request.target_status && original.every((path, index) => path === reordered[index]?.path)) return snapshot

      const candidate = candidateOrder(reordered, request.target_index)
      const planned = candidate === undefined
        ? reordered.map((task, index) => ({ task, status: task.path === selected.path ? request.target_status : undefined, order: (index + 1) * 100 }))
        : [{ task: selected, status: request.target_status, order: candidate }]
      const changes: Change[] = []
      for (const item of planned) {
        const path = await this.resolveTaskPath(snapshot.directory, item.task.path)
        const before = await readFile(path)
        if (versionOf(before) !== item.task.version) throw new StaleBoardError('A task changed on disk before the move could be committed.')
        changes.push({ path, before, after: renderProperties(before, item.status, item.order), mode: (await stat(path)).mode })
      }
      await this.commit(changes)
      return this.loadBoardUnlocked(snapshot.directory)
    })
  }

  private serialized<T>(operation: () => Promise<T>): Promise<T> {
    const result = this.queue.then(operation, operation)
    this.queue = result.then(() => undefined, () => undefined)
    return result
  }

  private async loadBoardUnlocked(directory: string): Promise<BoardResponse> {
    const root = await this.resolveDirectory(directory)
    const candidates = await markdownFiles(root)
    const tasks: TaskDto[] = []
    const warnings: BoardWarning[] = []
    const revisions: string[] = []
    for (const candidate of candidates) {
      const taskPath = relative(root, candidate).split(sep).join('/')
      try {
        const canonical = await realpath(candidate)
        if (!inside(root, canonical) || !(await stat(canonical)).isFile()) {
          warnings.push({ path: taskPath, message: 'Skipped file outside the selected task folder.' })
          revisions.push(`${taskPath}:unreadable`)
          continue
        }
        const bytes = await readFile(canonical)
        const version = versionOf(bytes)
        revisions.push(`${taskPath}:${version}`)
        const parsed = parseTask(taskPath, bytes)
        if (parsed.task) tasks.push(parsed.task)
        if (parsed.warning) warnings.push(parsed.warning)
      } catch (error) {
        warnings.push({ path: taskPath, message: `Could not read file: ${error instanceof Error ? error.message : String(error)}` })
        revisions.push(`${taskPath}:unreadable`)
      }
    }
    tasks.sort((a, b) => KANBAN_STATUSES.indexOf(a.status) - KANBAN_STATUSES.indexOf(b.status) || a.order - b.order || a.path.localeCompare(b.path, undefined, { sensitivity: 'base' }))
    warnings.push(...duplicateWarnings(tasks))
    return {
      directory: root,
      revision: createHash('sha256').update(revisions.join('\n')).digest('hex'),
      tasks,
      warnings,
    }
  }

  private async commit(changes: Change[]): Promise<void> {
    const replaced: Change[] = []
    try {
      for (const change of changes) {
        const current = await readFile(change.path)
        if (!current.equals(change.before)) throw new StaleBoardError('A task changed on disk before the move could be committed.')
        change.temporary = `${change.path}.${randomUUID()}.tmp`
        await writeDurable(change.temporary, change.after, change.mode)
      }
      for (const change of changes) {
        if (!(await readFile(change.path)).equals(change.before)) {
          throw new StaleBoardError('A task changed on disk before the move could be committed.')
        }
      }
      for (const change of changes) {
        await replaceWithRetry(this.files, change.temporary!, change.path)
        change.temporary = undefined
        replaced.push(change)
      }
    } catch (error) {
      let rollbackError: unknown
      for (const change of replaced.reverse()) {
        try {
          const temporary = `${change.path}.${randomUUID()}.rollback.tmp`
          await writeDurable(temporary, change.before, change.mode)
          await replaceWithRetry(this.files, temporary, change.path)
        } catch (caught) { rollbackError = caught }
      }
      for (const change of changes) if (change.temporary) await unlink(change.temporary).catch(() => undefined)
      if (error instanceof InvalidTaskError || error instanceof StaleBoardError) throw error
      throw new StorageError(`Could not commit task move${rollbackError ? ' and rollback was incomplete' : ''}.`, { cause: error })
    }
  }
}

function inside(root: string, path: string): boolean {
  const child = relative(root, path)
  return child === '' || (!child.startsWith(`..${sep}`) && child !== '..' && !isAbsolute(child))
}

async function markdownFiles(root: string): Promise<string[]> {
  const output: string[] = []
  async function visit(directory: string): Promise<void> {
    for (const entry of await readdir(directory, { withFileTypes: true })) {
      const path = join(directory, entry.name)
      if (entry.isDirectory()) await visit(path)
      else if ((entry.isFile() || entry.isSymbolicLink()) && entry.name.toLowerCase().endsWith('.md') && entry.name.toLowerCase() !== 'readme.md') output.push(path)
    }
  }
  await visit(root)
  return output.sort((a, b) => relative(root, a).localeCompare(relative(root, b), undefined, { sensitivity: 'base' }))
}

function candidateOrder(tasks: TaskDto[], index: number): number | undefined {
  const previous = index > 0 ? tasks[index - 1] : undefined
  const following = index + 1 < tasks.length ? tasks[index + 1] : undefined
  if (!previous && !following) return 100
  if (!previous) return following!.order > 1 ? Math.max(1, Math.floor(following!.order / 2)) : undefined
  if (!following) return previous.order + 100
  const gap = following.order - previous.order
  return gap > 1 ? previous.order + Math.floor(gap / 2) : undefined
}

function duplicateWarnings(tasks: TaskDto[]): BoardWarning[] {
  const groups = new Map<string, TaskDto[]>()
  for (const task of tasks) groups.set(`${task.status}\0${task.order}`, [...(groups.get(`${task.status}\0${task.order}`) ?? []), task])
  return [...groups.values()].flatMap((matches) => matches.length < 2 ? [] : matches.map((task) => ({ path: task.path, message: `Duplicate order ${task.order} in ${task.status}: ${matches.map((match) => match.path).join(', ')}.` })))
}

async function writeDurable(path: string, bytes: Buffer, mode: number): Promise<void> {
  await mkdir(dirname(path), { recursive: true })
  const handle = await open(path, 'wx', mode)
  try { await handle.writeFile(bytes); await handle.sync() } finally { await handle.close() }
  await chmod(path, mode)
}

async function replaceWithRetry(files: FileOperations, source: string, target: string): Promise<void> {
  for (let attempt = 0; ; attempt += 1) {
    try { await files.replace(source, target); return } catch (error) {
      const code = (error as NodeJS.ErrnoException).code
      if (attempt >= 3 || (code !== 'EPERM' && code !== 'EBUSY')) throw error
      await new Promise((resolveDelay) => setTimeout(resolveDelay, 20 * (attempt + 1)))
    }
  }
}
