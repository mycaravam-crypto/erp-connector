import { describe, it, expect, vi, beforeEach } from 'vitest'

// The router module creates its singleton router (and installs the beforeEach guard) at import time, so
// each test resets the module registry first and re-imports both the api mocks and the router fresh —
// otherwise a spy set on the previous module instance wouldn't reach the newly-imported router's guard.
async function freshRouter(opts: { loggedIn: boolean; connectionConfigured: boolean }) {
  vi.resetModules()
  const authApi = await import('@/api/auth')
  const connectionApi = await import('@/api/connection')
  vi.spyOn(authApi, 'isLoggedIn').mockReturnValue(opts.loggedIn)
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
