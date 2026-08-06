import { test, expect } from '@playwright/test'

test.describe('Audit Log', () => {
  test('shows audit entries after login', async ({ page }) => {
    // Login — this action itself writes an audit entry
    await page.goto('/login')
    await page.getByLabel(/username/i).fill('alice')
    await page.getByLabel(/password/i).fill('alice123')
    await page.getByRole('button', { name: /sign in/i }).click()
    await expect(page).toHaveURL(/\/connect/, { timeout: 5000 })

    // Navigate to audit log
    await page.getByRole('link', { name: /audit log/i }).click()
    await expect(page).toHaveURL(/\/audit/)

    // At least the login entry should appear (audit log accumulates rows across runs,
    // so 'login'/'alice' can match many cells — .first() just checks presence).
    await expect(page.getByText('login').first()).toBeVisible({ timeout: 5000 })
    await expect(page.getByText('alice').first()).toBeVisible()
  })
})
