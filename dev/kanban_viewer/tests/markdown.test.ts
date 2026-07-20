import { describe, expect, it } from 'vitest'
import { parseTask, renderProperties, versionOf } from '../server/markdown.js'

describe('Markdown codec', () => {
  it.each([
    ['# Task\n- status: Nope\n- order: 1\n', 'Unknown status'],
    ['# Task\n- status: Backlog\n- order: zero\n', 'positive integer'],
    ['- status: Backlog\n- order: 1\n', 'Missing H1'],
    ['# Task\n- status: Backlog\n- status: Ready\n- order: 1\n', "exactly one '- status:'"],
  ])('warns for malformed input', (text, message) => {
    expect(parseTask('task.md', Buffer.from(text)).warning?.message).toContain(message)
  })

  it('warns for invalid UTF-8 and preserves a missing final newline', () => {
    expect(parseTask('task.md', Buffer.from([0xff])).warning?.message).toContain('UTF-8')
    const bytes = Buffer.from('# Task\n- status: Backlog\n- order: 1')
    expect(renderProperties(bytes, 'Ready', 2).toString()).toBe('# Task\n- status: Ready\n- order: 2')
    expect(versionOf(bytes)).toHaveLength(16)
  })

  it('rejects rendering when required properties disappeared', () => {
    expect(() => renderProperties(Buffer.from('# Task\n'), 'Ready', 2)).toThrow('properties changed')
  })
})
