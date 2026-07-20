import { expect, test } from '@playwright/test'
import { mkdtemp, rm, writeFile } from 'node:fs/promises'
import { join } from 'node:path'
import { tmpdir } from 'node:os'

let directory: string

test.beforeEach(async ({ page }) => {
  directory = await mkdtemp(join(tmpdir(), 'kanban-e2e-'))
  await writeFile(join(directory, 'task.md'), '# Browser task\n\n- status: Backlog\n- order: 100\n\nBody.\n')
  await writeFile(join(directory, 'second.md'), '# Second task\n\n- status: Backlog\n- order: 200\n\nBody.\n')
  await writeFile(join(directory, 'ready.md'), '# Existing ready task\n\n- status: Ready\n- order: 100\n\nBody.\n')
  await writeFile(join(directory, 'ready-second.md'), '# Second ready task\n\n- status: Ready\n- order: 200\n\nBody.\n')
  await page.goto('/')
  await expect(page.getByRole('button', { name: 'Apply' })).toBeEnabled()
  await page.getByLabel('Task folder').fill(directory)
  await page.getByRole('button', { name: 'Apply' }).click()
  await expect(page.getByRole('button', { name: 'Browser task', exact: true })).toBeVisible()
})

test.afterEach(async () => rm(directory, { recursive: true, force: true }))

test('moves a task with its pointer handle and persists the selected folder', async ({ page }) => {
  const handle = page.getByRole('button', { name: 'Drag Browser task' })
  const target = page.getByRole('heading', { name: 'Ready' }).locator('..').locator('..')
  const secondReady = page.getByRole('button', { name: 'Second ready task', exact: true }).locator('..').locator('..')
  const sourceBox = await handle.boundingBox()
  const targetBox = await secondReady.boundingBox()
  expect(sourceBox).not.toBeNull()
  expect(targetBox).not.toBeNull()
  await page.mouse.move(sourceBox!.x + sourceBox!.width / 2, sourceBox!.y + sourceBox!.height / 2)
  await page.mouse.down()
  await page.mouse.move(sourceBox!.x + 20, sourceBox!.y + 20, { steps: 5 })
  await page.mouse.move(targetBox!.x + targetBox!.width / 2, targetBox!.y + 4, { steps: 15 })
  await page.mouse.up()
  await expect(target.getByRole('button', { name: 'Browser task', exact: true })).toBeVisible()
  await expect(target.locator('button[title^="Open"]')).toHaveText(['Existing ready task', 'Browser task', 'Second ready task'])

  await page.reload()
  await expect(page.getByLabel('Task folder')).toHaveValue(directory)
  await expect(target.getByRole('button', { name: 'Browser task', exact: true })).toBeVisible()
})

test('reorders cards within a populated column using the live insertion index', async ({ page }) => {
  const handle = page.getByRole('button', { name: 'Drag Browser task' })
  const second = page.getByRole('button', { name: 'Second task', exact: true }).locator('..').locator('..')
  const sourceBox = (await handle.boundingBox())!
  const targetBox = (await second.boundingBox())!
  await page.mouse.move(sourceBox.x + sourceBox.width / 2, sourceBox.y + sourceBox.height / 2)
  await page.mouse.down()
  await page.mouse.move(sourceBox.x + 20, sourceBox.y + 20, { steps: 5 })
  await page.mouse.move(targetBox.x + targetBox.width / 2, targetBox.y + targetBox.height - 4, { steps: 12 })
  await page.mouse.up()
  const backlog = page.getByRole('heading', { name: 'Backlog' }).locator('..').locator('..')
  await expect(backlog.locator('button[title^="Open"]')).toHaveText(['Second task', 'Browser task'])
})

test('supports keyboard drag cancellation without losing the card', async ({ page }) => {
  const handle = page.getByRole('button', { name: 'Drag Browser task' })
  await handle.focus()
  await page.keyboard.press('Space')
  await page.keyboard.press('Escape')
  await expect(page.getByRole('button', { name: 'Browser task', exact: true })).toBeVisible()
  await expect(handle).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Backlog' }).locator('..').locator('..').getByRole('button', { name: 'Browser task', exact: true })).toBeVisible()
})

test('moves a task with a trusted touch gesture', async ({ page, context }) => {
  const handle = page.getByRole('button', { name: 'Drag Browser task' })
  const target = page.getByRole('heading', { name: 'Started' }).locator('..').locator('..')
  const sourceBox = (await handle.boundingBox())!
  const targetBox = (await target.boundingBox())!
  const session = await context.newCDPSession(page)
  const source = { x: sourceBox.x + sourceBox.width / 2, y: sourceBox.y + sourceBox.height / 2 }
  const destination = { x: targetBox.x + targetBox.width / 2, y: targetBox.y + 120 }
  await session.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [source] })
  await page.waitForTimeout(400)
  for (let step = 1; step <= 12; step += 1) {
    await session.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [{
      x: source.x + (destination.x - source.x) * step / 12,
      y: source.y + (destination.y - source.y) * step / 12,
    }] })
  }
  await session.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] })
  await expect(target.getByRole('button', { name: 'Browser task', exact: true })).toBeVisible()
})
