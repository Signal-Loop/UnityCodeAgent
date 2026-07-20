import { createHash } from 'node:crypto'
import type { BoardWarning, KanbanStatus, TaskDto } from '../shared/contracts.js'
import { KANBAN_STATUSES } from '../shared/contracts.js'

const BOM = Buffer.from([0xef, 0xbb, 0xbf])
const titlePattern = /^#\s+(.+?)\s*$/gm
const statusPattern = /^- status:\s*(.*?)\s*$/gm
const orderPattern = /^- order:\s*(.*?)\s*$/gm
const goalPattern = /^- goal:\s*(.*?)\s*$/gm

function matches(pattern: RegExp, text: string): string[] {
  pattern.lastIndex = 0
  return [...text.matchAll(pattern)].map((match) => match[1])
}

export function versionOf(bytes: Buffer): string {
  return createHash('sha256').update(bytes).digest('hex').slice(0, 16)
}

export function parseTask(path: string, bytes: Buffer): { task?: TaskDto; warning?: BoardWarning } {
  let text: string
  try {
    text = new TextDecoder('utf-8', { fatal: true }).decode(bytes.subarray(bytes.subarray(0, 3).equals(BOM) ? 3 : 0))
  } catch {
    return { warning: { path, message: 'File is not valid UTF-8.' } }
  }
  const titles = matches(titlePattern, text)
  const statuses = matches(statusPattern, text)
  const orders = matches(orderPattern, text)
  const goals = matches(goalPattern, text)
  if (!titles.length) return { warning: { path, message: 'Missing H1 task title.' } }
  if (statuses.length !== 1) return { warning: { path, message: "Expected exactly one '- status:' property." } }
  if (orders.length !== 1) return { warning: { path, message: "Expected exactly one '- order:' property." } }
  const status = statuses[0].trim()
  if (!(KANBAN_STATUSES as readonly string[]).includes(status)) {
    return { warning: { path, message: `Unknown status: ${status || '(empty)'}.` } }
  }
  const order = Number(orders[0].trim())
  if (!Number.isInteger(order) || order <= 0) {
    return { warning: { path, message: 'Order must be a positive integer.' } }
  }
  return {
    task: {
      path,
      title: titles[0].trim(),
      goal: goals.length ? goals[0].trim() : null,
      status: status as KanbanStatus,
      order,
      version: versionOf(bytes),
    },
  }
}

export function renderProperties(bytes: Buffer, status: KanbanStatus | undefined, order: number): Buffer {
  const hasBom = bytes.subarray(0, 3).equals(BOM)
  const text = bytes.subarray(hasBom ? 3 : 0).toString('utf8')
  let statusReplaced = status === undefined
  let orderReplaced = false
  const rendered = text.replace(/^.*(?:\r\n|\n|\r|$)/gm, (line) => {
    const ending = line.match(/(?:\r\n|\n|\r)$/)?.[0] ?? ''
    const content = line.slice(0, line.length - ending.length)
    if (!statusReplaced && /^- status:\s*.*$/.test(content)) {
      statusReplaced = true
      return `- status: ${status}${ending}`
    }
    if (!orderReplaced && /^- order:\s*.*$/.test(content)) {
      orderReplaced = true
      return `- order: ${order}${ending}`
    }
    return line
  })
  if (!statusReplaced || !orderReplaced) throw new Error('Task properties changed before the move.')
  return Buffer.concat([hasBom ? BOM : Buffer.alloc(0), Buffer.from(rendered)])
}
