import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { createApp } from './app.js'

const currentDirectory = dirname(fileURLToPath(import.meta.url))
const workspaceRoot = resolve(currentDirectory, '../../..')
const app = createApp({ workspaceRoot, logger: true })

try {
  await app.listen({ host: '127.0.0.1', port: 8765 })
} catch (error) {
  app.log.error(error)
  process.exitCode = 1
}
