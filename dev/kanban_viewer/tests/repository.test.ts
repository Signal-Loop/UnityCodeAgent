import { readFile, symlink, writeFile } from 'node:fs/promises'
import { join } from 'node:path'
import { afterEach, describe, expect, it } from 'vitest'
import { InvalidDirectoryError, InvalidTaskError, KanbanRepository, StaleBoardError, StorageError, type FileOperations } from '../server/repository.js'
import { temporaryDirectory, writeTask } from './helpers.js'

const cleanups: Array<() => Promise<void>> = []
afterEach(async () => Promise.all(cleanups.splice(0).map((cleanup) => cleanup())))

async function board() {
  const temporary = await temporaryDirectory()
  cleanups.push(temporary.cleanup)
  return temporary.path
}

describe('KanbanRepository', () => {
  it('recurses, sorts, warns, and ignores README files', async () => {
    const root = await board()
    await writeTask(root, 'z.md', 'Backlog', 200)
    await writeTask(root, 'nested/a.md', 'Backlog', 100)
    await writeTask(root, 'done.md', 'Completed', 100)
    await writeFile(join(root, 'README.md'), 'not a task')
    await writeFile(join(root, 'broken.md'), '# Broken\n- status: Backlog\n')
    const snapshot = await new KanbanRepository(root).loadBoard(root)
    expect(snapshot.tasks.map((task) => task.path)).toEqual(['nested/a.md', 'z.md', 'done.md'])
    expect(snapshot.warnings).toEqual([{ path: 'broken.md', message: "Expected exactly one '- order:' property." }])
  })

  it('uses sparse gaps and rewrites only the selected task', async () => {
    const root = await board()
    const movedPath = await writeTask(root, 'move.md', 'Backlog', 100)
    const onePath = await writeTask(root, 'one.md', 'Started', 100)
    await writeTask(root, 'two.md', 'Started', 200)
    const beforeOne = await readFile(onePath)
    const repository = new KanbanRepository(root)
    const before = await repository.loadBoard(root)
    const after = await repository.moveTask({ directory: root, task_path: 'move.md', target_status: 'Started', target_index: 1, revision: before.revision })
    expect(after.tasks.find((task) => task.path === 'move.md')).toMatchObject({ status: 'Started', order: 150 })
    expect(await readFile(onePath)).toEqual(beforeOne)
    expect((await readFile(movedPath, 'utf8'))).toContain('Body stays unchanged.')
  })

  it('normalizes only the target column when there is no integer gap', async () => {
    const root = await board()
    await writeTask(root, 'move.md', 'Backlog', 50)
    await writeTask(root, 'one.md', 'Ready', 1)
    await writeTask(root, 'two.md', 'Ready', 2)
    const repository = new KanbanRepository(root)
    const before = await repository.loadBoard(root)
    const after = await repository.moveTask({ directory: root, task_path: 'move.md', target_status: 'Ready', target_index: 1, revision: before.revision })
    expect(after.tasks.filter((task) => task.status === 'Ready').map(({ path, order }) => [path, order])).toEqual([['one.md', 100], ['move.md', 200], ['two.md', 300]])
  })

  it('preserves BOM and CRLF and rejects stale revisions', async () => {
    const root = await board()
    const path = await writeTask(root, 'task.md', 'Backlog', 100, { bom: true, newline: '\r\n' })
    const repository = new KanbanRepository(root)
    const before = await repository.loadBoard(root)
    await writeFile(path, Buffer.concat([await readFile(path), Buffer.from('external')]))
    await expect(repository.moveTask({ directory: root, task_path: 'task.md', target_status: 'Started', target_index: 0, revision: before.revision })).rejects.toBeInstanceOf(StaleBoardError)
    const refreshed = await repository.loadBoard(root)
    await repository.moveTask({ directory: root, task_path: 'task.md', target_status: 'Started', target_index: 0, revision: refreshed.revision })
    const bytes = await readFile(path)
    expect(bytes.subarray(0, 3)).toEqual(Buffer.from([0xef, 0xbb, 0xbf]))
    expect(bytes.toString()).toContain('\r\n')
  })

  it('serializes simultaneous moves so only one revision wins', async () => {
    const root = await board()
    await writeTask(root, 'one.md')
    await writeTask(root, 'two.md', 'Backlog', 200)
    const repository = new KanbanRepository(root)
    const snapshot = await repository.loadBoard(root)
    const results = await Promise.allSettled([
      repository.moveTask({ directory: root, task_path: 'one.md', target_status: 'Ready', target_index: 0, revision: snapshot.revision }),
      repository.moveTask({ directory: root, task_path: 'two.md', target_status: 'Ready', target_index: 0, revision: snapshot.revision }),
    ])
    expect(results.filter((result) => result.status === 'fulfilled')).toHaveLength(1)
    expect(results.find((result) => result.status === 'rejected' && result.reason instanceof StaleBoardError)).toBeDefined()
  })

  it('rolls back earlier replacements after a multi-file commit failure', async () => {
    const root = await board()
    const one = await writeTask(root, 'one.md', 'Ready', 1)
    const two = await writeTask(root, 'two.md', 'Ready', 2)
    await writeTask(root, 'move.md', 'Backlog', 100)
    const originals = [await readFile(one), await readFile(two)]
    let replacements = 0
    const files: FileOperations = {
      async replace(source, target) {
        replacements += 1
        if (replacements === 2) throw Object.assign(new Error('injected'), { code: 'EIO' })
        await import('node:fs/promises').then((fs) => fs.rename(source, target))
      },
    }
    const repository = new KanbanRepository(root, files)
    const snapshot = await repository.loadBoard(root)
    await expect(repository.moveTask({ directory: root, task_path: 'move.md', target_status: 'Ready', target_index: 1, revision: snapshot.revision })).rejects.toBeInstanceOf(StorageError)
    expect(await readFile(one)).toEqual(originals[0])
    expect(await readFile(two)).toEqual(originals[1])
  })

  it('warns and skips symlinks that escape the selected root', async () => {
    const root = await board()
    const other = await board()
    const outside = await writeTask(other, 'outside.md')
    try { await symlink(outside, join(root, 'linked.md')) } catch { return }
    const snapshot = await new KanbanRepository(root).loadBoard(root)
    expect(snapshot.tasks).toHaveLength(0)
    expect(snapshot.warnings[0]?.message).toContain('outside')
  })

  it('validates directories, paths, indexes, and statuses', async () => {
    const root = await board()
    await writeTask(root, 'task.md')
    const repository = new KanbanRepository(root)
    await expect(repository.resolveDirectory('missing')).rejects.toBeInstanceOf(InvalidDirectoryError)
    await expect(repository.resolveTaskPath(root, '../task.md')).rejects.toBeInstanceOf(InvalidTaskError)
    const snapshot = await repository.loadBoard(root)
    await expect(repository.moveTask({ directory: root, task_path: 'task.md', target_status: 'Ready', target_index: 2, revision: snapshot.revision })).rejects.toBeInstanceOf(InvalidTaskError)
  })

  it('allocates orders at the beginning, end, and in an empty column and recognizes no-ops', async () => {
    const root = await board()
    await writeTask(root, 'one.md', 'Backlog', 100)
    await writeTask(root, 'two.md', 'Backlog', 200)
    const repository = new KanbanRepository(root)
    let snapshot = await repository.loadBoard(root)
    const noOp = await repository.moveTask({ directory: root, task_path: 'one.md', target_status: 'Backlog', target_index: 0, revision: snapshot.revision })
    expect(noOp.revision).toBe(snapshot.revision)
    snapshot = await repository.moveTask({ directory: root, task_path: 'two.md', target_status: 'Ready', target_index: 0, revision: snapshot.revision })
    expect(snapshot.tasks.find((task) => task.path === 'two.md')?.order).toBe(100)
    snapshot = await repository.moveTask({ directory: root, task_path: 'two.md', target_status: 'Backlog', target_index: 0, revision: snapshot.revision })
    expect(snapshot.tasks.find((task) => task.path === 'two.md')?.order).toBe(50)
    snapshot = await repository.moveTask({ directory: root, task_path: 'two.md', target_status: 'Backlog', target_index: 1, revision: snapshot.revision })
    expect(snapshot.tasks.find((task) => task.path === 'two.md')?.order).toBe(200)
  })

  it('retries transient replacement failures', async () => {
    const root = await board()
    await writeTask(root, 'task.md')
    let calls = 0
    const files: FileOperations = { async replace(source, target) {
      calls += 1
      if (calls < 3) throw Object.assign(new Error('busy'), { code: 'EBUSY' })
      await import('node:fs/promises').then((fs) => fs.rename(source, target))
    } }
    const repository = new KanbanRepository(root, files)
    const snapshot = await repository.loadBoard(root)
    const after = await repository.moveTask({ directory: root, task_path: 'task.md', target_status: 'Ready', target_index: 0, revision: snapshot.revision })
    expect(calls).toBe(3)
    expect(after.tasks[0].status).toBe('Ready')
  })
})
