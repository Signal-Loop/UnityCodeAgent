import Fastify, { type FastifyBaseLogger, type FastifyError, type FastifyInstance, type FastifyReply } from 'fastify'
import { join } from 'node:path'
import { TypeBoxTypeProvider } from '@fastify/type-provider-typebox'
import {
  BoardResponseSchema,
  ConfigResponseSchema,
  DirectoryQuerySchema,
  ErrorResponseSchema,
  KANBAN_STATUSES,
  MoveTaskRequestSchema,
  OpenTaskRequestSchema,
} from '../shared/contracts.js'
import { Type } from '@sinclair/typebox'
import type { EditorLauncher } from './editorLauncher.js'
import { VsCodeEditorLauncher } from './editorLauncher.js'
import { InvalidDirectoryError, InvalidTaskError, KanbanRepository, StaleBoardError, StorageError } from './repository.js'
import { BoardWatchService } from './watchService.js'

export interface AppDependencies {
  workspaceRoot: string
  repository?: KanbanRepository
  editorLauncher?: EditorLauncher
  watchService?: BoardWatchService
  logger?: boolean
  heartbeatMs?: number
}

export function createApp(dependencies: AppDependencies): FastifyInstance {
  const app = Fastify({ logger: dependencies.logger ?? false }).withTypeProvider<TypeBoxTypeProvider>()
  const repository = dependencies.repository ?? new KanbanRepository(dependencies.workspaceRoot)
  const editor = dependencies.editorLauncher ?? new VsCodeEditorLauncher()
  const watches = dependencies.watchService ?? new BoardWatchService(undefined, (directory, error) => app.log.error({ directory, error }, 'Board watcher failed'))

  app.get('/api/config', { schema: { response: { 200: ConfigResponseSchema } } }, async () => ({
    workspace_root: repository.workspaceRoot,
    default_task_directory: join(repository.workspaceRoot, 'docs', 'kanban'),
    statuses: [...KANBAN_STATUSES],
  }))

  app.get('/api/board', { schema: { querystring: DirectoryQuerySchema, response: { 200: BoardResponseSchema, 400: ErrorResponseSchema } } }, async (request, reply) => {
    try {
      const board = await repository.loadBoard(request.query.directory)
      request.log.debug({ directory: board.directory, taskCount: board.tasks.length, warningCount: board.warnings.length }, 'Loaded kanban board')
      return board
    } catch (error) { return sendKnownError(reply, error, request.log) }
  })

  app.patch('/api/board/tasks', { schema: { body: MoveTaskRequestSchema, response: { 200: BoardResponseSchema, 400: ErrorResponseSchema, 409: ErrorResponseSchema, 422: ErrorResponseSchema, 500: ErrorResponseSchema } } }, async (request, reply) => {
    try {
      const board = await repository.moveTask(request.body)
      request.log.info({ taskPath: request.body.task_path, targetStatus: request.body.target_status }, 'Moved kanban task')
      return board
    } catch (error) { return sendKnownError(reply, error, request.log) }
  })

  app.post('/api/editor/open', { schema: { body: OpenTaskRequestSchema, response: { 204: Type.Null(), 400: ErrorResponseSchema, 422: ErrorResponseSchema, 503: ErrorResponseSchema } } }, async (request, reply) => {
    try {
      const directory = await repository.resolveDirectory(request.body.directory)
      const taskPath = await repository.resolveTaskPath(directory, request.body.task_path)
      await editor.open(taskPath)
      request.log.info({ taskPath: request.body.task_path }, 'Opened kanban task in editor')
      return reply.code(204).send(null)
    } catch (error) {
      if (error instanceof InvalidDirectoryError || error instanceof InvalidTaskError) return sendKnownError(reply, error, request.log)
      return reply.code(503).send({ detail: error instanceof Error ? error.message : 'Could not launch VS Code.' })
    }
  })

  app.get('/api/events', { schema: { querystring: DirectoryQuerySchema } }, async (request, reply) => {
    let directory: string
    try { directory = await repository.resolveDirectory(request.query.directory) }
    catch (error) { return sendKnownError(reply, error, request.log) }
    reply.hijack()
    const response = reply.raw
    response.writeHead(200, {
      'Content-Type': 'text/event-stream',
      'Cache-Control': 'no-cache',
      Connection: 'keep-alive',
      'X-Accel-Buffering': 'no',
    })
    response.write('retry: 1000\n\n')
    request.log.debug({ directory }, 'Opened board event subscription')
    const unsubscribe = watches.subscribe(directory, () => response.write('event: board-changed\ndata: refresh\n\n'))
    const heartbeat = setInterval(() => response.write(': heartbeat\n\n'), dependencies.heartbeatMs ?? 15_000)
    request.raw.once('close', () => {
      clearInterval(heartbeat)
      unsubscribe()
      request.log.debug({ directory }, 'Closed board event subscription')
    })
  })

  app.setErrorHandler((error: FastifyError, _request, reply) => {
    if (error.validation) return reply.code(error.validationContext === 'querystring' ? 400 : 422).send({ detail: error.message })
    app.log.error(error)
    return reply.code(500).send({ detail: 'An unexpected server error occurred.' })
  })
  app.addHook('onClose', async () => watches.close())
  return app
}

function sendKnownError(reply: FastifyReply, error: unknown, log?: FastifyBaseLogger) {
  if (error instanceof InvalidDirectoryError) return reply.code(400).send({ detail: error.message })
  if (error instanceof StaleBoardError) {
    log?.warn({ error }, 'Rejected stale kanban board mutation')
    return reply.code(409).send({ detail: error.message })
  }
  if (error instanceof InvalidTaskError) return reply.code(422).send({ detail: error.message })
  if (error instanceof StorageError) {
    log?.error({ error }, 'Kanban storage transaction failed')
    return reply.code(500).send({ detail: error.message })
  }
  throw error
}
