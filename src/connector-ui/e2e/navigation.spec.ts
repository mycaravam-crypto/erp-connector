import { test, expect } from '@playwright/test'

// Helper: log in as alice before each test
async function loginAs(page: import('@playwright/test').Page, user = 'alice', pass = 'alice123') {
  await page.goto('/login')
  await page.getByLabel(/username/i).fill(user)
  await page.getByLabel(/password/i).fill(pass)
  await page.getByRole('button', { name: /sign in/i }).click()
  await expect(page).toHaveURL(/\/connect/, { timeout: 5000 })
}

test.describe('Navigation', () => {
  test('shows step nav when logged in', async ({ page }) => {
    await loginAs(page)
    await expect(page.getByText('Connect')).toBeVisible()
    await expect(page.getByText('Source Schema')).toBeVisible()
    await expect(page.getByText('Export Schema')).toBeVisible()
    await expect(page.getByText('Export')).toBeVisible()
  })

  test('unknown route shows 404 page', async ({ page }) => {
    await loginAs(page)
    await page.goto('/this-does-not-exist')
    await expect(page.getByText('404')).toBeVisible()
  })

  test('navigates to Settings', async ({ page }) => {
    await loginAs(page)
    await page.getByRole('link', { name: /settings/i }).click()
    await expect(page).toHaveURL(/\/settings/)
  })

  test('navigates to Audit Log', async ({ page }) => {
    await loginAs(page)
    await page.getByRole('link', { name: /audit log/i }).click()
    await expect(page).toHaveURL(/\/audit/)
  })

  test('navigates to Export Runs', async ({ page }) => {
    await loginAs(page)
    // Click step 4 — Export
    await page.getByRole('link', { name: /export/i }).nth(0).click()
    await expect(page).toHaveURL(/\/exports/)
  })
})
