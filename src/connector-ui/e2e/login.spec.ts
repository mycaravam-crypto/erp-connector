import { test, expect } from '@playwright/test'

test.describe('Login', () => {
  test('redirects unauthenticated user to /login', async ({ page }) => {
    await page.goto('/')
    await expect(page).toHaveURL(/\/login/)
  })

  test('shows error for wrong password', async ({ page }) => {
    await page.goto('/login')
    await page.getByLabel(/username/i).fill('alice')
    await page.getByLabel(/password/i).fill('wrongpassword')
    await page.getByRole('button', { name: /sign in/i }).click()
    await expect(page.getByText(/invalid/i)).toBeVisible({ timeout: 5000 })
  })

  test('logs in with valid credentials and lands on connect page', async ({ page }) => {
    await page.goto('/login')
    await page.getByLabel(/username/i).fill('alice')
    await page.getByLabel(/password/i).fill('alice123')
    await page.getByRole('button', { name: /sign in/i }).click()
    await expect(page).toHaveURL(/\/connect/, { timeout: 5000 })
  })

  test('sign out clears session and redirects to login', async ({ page }) => {
    await page.goto('/login')
    await page.getByLabel(/username/i).fill('alice')
    await page.getByLabel(/password/i).fill('alice123')
    await page.getByRole('button', { name: /sign in/i }).click()
    await expect(page).toHaveURL(/\/connect/, { timeout: 5000 })
    await page.getByRole('button', { name: /sign out/i }).click()
    await expect(page).toHaveURL(/\/login/, { timeout: 5000 })
  })
})
