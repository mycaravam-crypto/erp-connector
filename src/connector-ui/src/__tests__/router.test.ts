import { describe, it, expect, vi, beforeEach } from 'vitest'

// The router module creates its singleton router (and installs the beforeEach guard) at import time, so
// each test resets the module registry first and re-imports both the api mocks and the router fresh —
// otherwise a spy set on the previous module instance wouldn't reach the newly-imported router's guard.
const ADMIN = { username: 'alice', role: 'Admin' as const, permissions: [] as string[] }

async function freshRouter(opts: {
  loggedIn: boolean
  connectionConfigured: boolean
  user?: { username: string; role: 'Admin' | 'User'; permissions: string[] } | null
}) {
  vi.resetModules()
  const authApi = await import('@/api/auth')
  const connectionApi = await import('@/api/connection')
  vi.spyOn(authApi, 'isLoggedIn').mockReturnValue(opts.loggedIn)
  vi.spyOn(authApi, 'fetchCurrentUser').mockResolvedValue(opts.user === undefined ? ADMIN : opts.user)
  vi.spyOn(authApi, 'clearSession').mockImplementation(() => {})
  vi.spyOn(connectionApi, 'isConnectionConfigured').mockResolvedValue(opts.connectionConfigured)
  const mod = await import('@/router/index')
  return mod.default
}

beforeEach(() => vi.restoreAllMocks())

describe('router — dashboard root route', () => {
  it('redirects / to /connect when no connection is configured', async () => {
    const router = await freshRouter({ loggedIn: true, connectionConfigured: false })
    await router.push('/')
    expect(router.currentRoute.value.name).toBe('connect')
    expect(router.currentRoute.value.query.notice).toBe('needs-connection')
  })

  it('stays on the dashboard when a connection is configured', async () => {
    const router = await freshRouter({ loggedIn: true, connectionConfigured: true })
    await router.push('/')
    expect(router.currentRoute.value.name).toBe('dashboard')
  })

  it('still redirects to login first when logged out, even with a connection configured', async () => {
    const router = await freshRouter({ loggedIn: false, connectionConfigured: true })
    await router.push('/')
    expect(router.currentRoute.value.name).toBe('login')
  })
})

describe('router — permissions', () => {
  const carol = (permissions: string[]) => ({ username: 'carol', role: 'User' as const, permissions })

  it('sends a User to the dashboard instead of an admin-only route', async () => {
    const router = await freshRouter({ loggedIn: true, connectionConfigured: true, user: carol([]) })
    await router.push('/connect')
    expect(router.currentRoute.value.name).toBe('dashboard')
  })

  it('lets a User open a route they hold the permission for', async () => {
    const router = await freshRouter({
      loggedIn: true,
      connectionConfigured: true,
      user: carol(['exportJobs.view']),
    })
    await router.push('/export-definitions')
    expect(router.currentRoute.value.name).toBe('export-definitions')
  })

  it('requires the create permission for a "new" page, view for an existing one', async () => {
    const router = await freshRouter({
      loggedIn: true,
      connectionConfigured: true,
      user: carol(['exportJobs.view']),
    })
    await router.push('/export-definitions/new')
    expect(router.currentRoute.value.name).toBe('dashboard')
    await router.push('/export-definitions/7')
    expect(router.currentRoute.value.name).toBe('export-definition-edit')
  })

  it('does not send a User without a connection to Connect, which only Admins can open', async () => {
    const router = await freshRouter({ loggedIn: true, connectionConfigured: false, user: carol([]) })
    await router.push('/')
    expect(router.currentRoute.value.name).toBe('dashboard')
  })

  it('signs out when the session is no longer valid', async () => {
    const router = await freshRouter({ loggedIn: true, connectionConfigured: true, user: null })
    await router.push('/')
    expect(router.currentRoute.value.name).toBe('login')
  })
})
