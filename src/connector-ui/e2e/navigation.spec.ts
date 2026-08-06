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
    // Scoped to <nav> — 'Connect' etc. also appear in the page heading, banners, and buttons.
    const links = page.getByRole('navigation').getByRole('link')
    await expect(links).toHaveCount(4)
    await expect(links.nth(0)).toContainText('Connect')
    await expect(links.nth(1)).toContainText('Source Schema')
    await expect(links.nth(2)).toContainText('Export Schema')
    await expect(links.nth(3)).toContainText('Export')
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
    // Step 4 — Export. Index-based because step 3 "Export Schema" also matches /export/i,
    // and was nth(0) in DOM order — the previous version of this test clicked the wrong link.
    await page.getByRole('navigation').getByRole('link').nth(3).click()
    await expect(page).toHaveURL(/\/exports/)
  })
})
