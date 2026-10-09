import { test, expect, type Page } from '@playwright/test'

// Roles and permissions against the running dev stack: alice is an Admin, carol (carol123) a User — see
// DevAuthSeed. Each test that changes the User role's permissions puts the defaults back.
async function loginAs(page: Page, user: string, pass: string) {
  await page.goto('/login')
  await page.getByLabel(/username/i).fill(user)
  await page.getByLabel(/password/i).fill(pass)
  await page.getByRole('button', { name: /sign in/i }).click()
  await expect(page).not.toHaveURL(/\/login/, { timeout: 5000 })
}

const DEFAULT_USER_PERMISSIONS = [
  'sourceSchema.view',
  'exportMapping.view',
  'managedExport.view',
  'exportJobs.view',
  'importJobs.view',
  'icdSchema.view',
]

async function setUserPermissions(page: Page, permissions: string[]) {
  const token = await page.evaluate(() => sessionStorage.getItem('connector_token'))
  const res = await page.request.put('/api/settings/permissions', {
    headers: { Authorization: `Bearer ${token}` },
    data: { userPermissions: permissions },
  })
  expect(res.ok()).toBe(true)
}

test.describe('Roles and permissions', () => {
  test('a User sees only the menu items they may open, and no admin-only page', async ({ page }) => {
    await loginAs(page, 'carol', 'carol123')
    const nav = page.getByRole('navigation', { name: 'Connector' }).first()
    await expect(nav.getByRole('link', { name: 'Export Jobs' })).toBeVisible()
    await expect(nav.getByRole('link', { name: 'Connect' })).toHaveCount(0)

    await page.goto('/connect')
    await expect(page).toHaveURL(/\/$/)
    await page.goto('/settings')
    await expect(page).toHaveURL(/\/$/)

    await page.goto('/export-definitions')
    await expect(page.getByRole('link', { name: '+ New' })).toHaveCount(0)
  })

  test('an Admin grants a permission and the User gets it on their next click', async ({ browser }) => {
    const admin = await browser.newPage()
    await loginAs(admin, 'alice', 'alice123')
    await admin.goto('/settings')
    await expect(admin.getByRole('heading', { name: 'Permissions' })).toBeVisible()
    await expect(admin.getByRole('heading', { name: 'Users' })).toBeVisible()

    const user = await browser.newPage()
    await loginAs(user, 'carol', 'carol123')
    await user.goto('/export-definitions')
    await expect(user.getByRole('link', { name: '+ New' })).toHaveCount(0)

    try {
      await setUserPermissions(admin, [...DEFAULT_USER_PERMISSIONS, 'exportJobs.create'])
      await user.getByRole('link', { name: 'Import Jobs' }).first().click()
      await user.getByRole('link', { name: 'Export Jobs' }).first().click()
      await expect(user.getByRole('link', { name: '+ New' })).toBeVisible()
    } finally {
      await setUserPermissions(admin, DEFAULT_USER_PERMISSIONS)
    }
  })
})
