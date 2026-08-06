import { test, expect } from '@playwright/test'

async function loginAs(page: import('@playwright/test').Page, user = 'alice', pass = 'alice123') {
  await page.goto('/login')
  await page.getByLabel(/username/i).fill(user)
  await page.getByLabel(/password/i).fill(pass)
  await page.getByRole('button', { name: /sign in/i }).click()
  await expect(page).toHaveURL(/\/connect/, { timeout: 5000 })
}

test.describe('Connection (Step 1)', () => {
  test('shows Step 1 heading and all form fields', async ({ page }) => {
    await loginAs(page)
    await expect(page.getByText('Step 1')).toBeVisible()
    await expect(page.getByRole('heading', { name: /connect to source database/i })).toBeVisible()
    await expect(page.locator('#host')).toBeVisible()
    await expect(page.locator('#port')).toBeVisible()
    await expect(page.locator('#database')).toBeVisible()
    await expect(page.locator('#username')).toBeVisible()
    await expect(page.locator('#password')).toBeVisible()
  })

  test('blocks submission and shows error for out-of-range port', async ({ page }) => {
    await loginAs(page)
    await page.locator('#port').fill('99999')
    await expect(page.getByText(/port must be a number between 1 and 65535/i)).toBeVisible()
    await page.getByRole('button', { name: /test connection/i }).click()
    await expect(page.getByText(/port must be a number between 1 and 65535/i).first()).toBeVisible()
    // No in-flight request should have started.
    await expect(page.getByRole('button', { name: /testing/i })).toHaveCount(0)
  })

  test('blocks submission and shows error for non-numeric port', async ({ page }) => {
    await loginAs(page)
    await page.locator('#port').fill('abc')
    await expect(page.getByText(/port must be a number between 1 and 65535/i)).toBeVisible()
  })

  test('shows backend validation error when required fields are blank', async ({ page }) => {
    await loginAs(page)
    await page.locator('#host').fill('')
    await page.locator('#database').fill('')
    await page.locator('#username').fill('')
    await page.getByRole('button', { name: /test connection/i }).click()
    await expect(page.getByText(/host, database, and username are required/i)).toBeVisible({ timeout: 5000 })
  })

  test('shows connection failed error for an unreachable host', async ({ page }) => {
    await loginAs(page)
    await page.locator('#host').fill('nonexistent-host-e2e-test.invalid')
    await page.locator('#port').fill('5432')
    await page.locator('#database').fill('somedb')
    await page.locator('#username').fill('someuser')
    await page.locator('#password').fill('somepass')
    await page.getByRole('button', { name: /test connection/i }).click()
    await expect(page.getByText(/connection failed/i)).toBeVisible({ timeout: 15000 })
  })

  test('"Proceed" respects the connection route guard', async ({ page }) => {
    await loginAs(page)
    const isConnected = await page.getByText('Connected:').isVisible()
    await page.getByRole('button', { name: /proceed to source schema/i }).click()
    if (isConnected) {
      await expect(page).toHaveURL(/\/source-schema/)
    } else {
      await expect(page).toHaveURL(/\/connect\?notice=needs-connection/)
      await expect(page.getByText(/a database connection is required/i)).toBeVisible()
    }
  })
})
