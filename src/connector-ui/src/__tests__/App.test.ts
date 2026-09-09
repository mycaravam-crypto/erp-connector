import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import App from '@/App.vue'
import * as authApi from '@/api/auth'

async function buildRouter(initialPath: string) {
  const r = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', name: 'dashboard', component: { template: '<div/>' } },
      { path: '/connect', name: 'connect', component: { template: '<div/>' } },
      { path: '/source-schema', name: 'source-schema', component: { template: '<div/>' } },
      { path: '/export-schema', name: 'export-schema', component: { template: '<div/>' } },
      { path: '/exports', name: 'exports', component: { template: '<div/>' } },
      { path: '/exports/:seqNo', name: 'export-detail', component: { template: '<div/>' } },
      { path: '/settings', name: 'settings', component: { template: '<div/>' } },
      { path: '/icd-schema', name: 'icd-schema', component: { template: '<div/>' } },
      { path: '/audit', name: 'audit', component: { template: '<div/>' } },
      { path: '/export-definitions', name: 'export-definitions', component: { template: '<div/>' } },
      { path: '/import-definitions', name: 'import-definitions', component: { template: '<div/>' } },
      { path: '/login', name: 'login', component: { template: '<div/>' } },
    ],
  })
  await r.push(initialPath)
  return r
}

beforeEach(() => {
  vi.restoreAllMocks()
  vi.spyOn(authApi, 'isLoggedIn').mockReturnValue(true)
  vi.spyOn(authApi, 'getUsername').mockReturnValue('alice')
})

describe('App shell', () => {
  it('marks the pill matching the current route as active, others inactive', async () => {
    const w = mount(App, { global: { plugins: [await buildRouter('/export-schema')] } })
    await flushPromises()

    const links = w.findAll('nav[aria-label="Connector"] a')
    expect(links).toHaveLength(6)
    expect(links[0]!.classes()).not.toContain('bg-nav-hover')
    expect(links[2]!.classes()).toContain('bg-nav-hover')
  })

  it('highlights Managed Export when on that area, Export Jobs/Import Jobs alongside it inactive', async () => {
    const w = mount(App, { global: { plugins: [await buildRouter('/exports')] } })
    await flushPromises()

    const links = w.findAll('nav[aria-label="Connector"] a')
    expect(links[3]!.text()).toContain('Managed Export')
    expect(links[3]!.classes()).toContain('bg-nav-hover')
    expect(links[4]!.text()).toContain('Export Jobs')
    expect(links[5]!.text()).toContain('Import Jobs')
    expect(links[4]!.classes()).not.toContain('bg-nav-hover')
    expect(links[5]!.classes()).not.toContain('bg-nav-hover')
  })

  it('highlights the Export Jobs pill when on that area', async () => {
    const w = mount(App, { global: { plugins: [await buildRouter('/export-definitions')] } })
    await flushPromises()

    const links = w.findAll('nav[aria-label="Connector"] a')
    expect(links[4]!.text()).toContain('Export Jobs')
    expect(links[4]!.classes()).toContain('bg-nav-hover')
  })

  it('treats the export-detail route as the Managed Export pill being active', async () => {
    const w = mount(App, { global: { plugins: [await buildRouter('/exports/7')] } })
    await flushPromises()

    const links = w.findAll('nav[aria-label="Connector"] a')
    expect(links[3]!.classes()).toContain('bg-nav-hover')
  })

  it('shows no active pill on a secondary page', async () => {
    const w = mount(App, { global: { plugins: [await buildRouter('/settings')] } })
    await flushPromises()

    const links = w.findAll('nav[aria-label="Connector"] a')
    for (const link of links) {
      expect(link.classes()).not.toContain('bg-nav-hover')
    }
  })

  it('tucks the secondary nav inside the collapsed user menu until opened', async () => {
    const w = mount(App, { global: { plugins: [await buildRouter('/connect')] } })
    await flushPromises()

    expect(w.find('nav[aria-label="Secondary"]').exists()).toBe(false)

    // ConnectorNav's mobile hamburger toggle also carries aria-haspopup="menu", so target the
    // account menu's button by its accessible name (the username) instead of the generic selector.
    await w.findAll('button[aria-haspopup="menu"]').find((b) => b.text().includes('alice'))!.trigger('click')

    const secondary = w.find('nav[aria-label="Secondary"]')
    expect(secondary.exists()).toBe(true)
    expect(secondary.text()).toContain('ICD Schema')
    expect(secondary.text()).toContain('Settings')
    expect(secondary.text()).toContain('Audit Log')
    // Export Jobs/Import Jobs moved to the top-level Connector nav, not the account menu
    expect(secondary.text()).not.toContain('Export Jobs')
  })

  it('does not render the connector nav when logged out', async () => {
    vi.spyOn(authApi, 'isLoggedIn').mockReturnValue(false)
    const w = mount(App, { global: { plugins: [await buildRouter('/login')] } })
    await flushPromises()

    expect(w.find('nav[aria-label="Connector"]').exists()).toBe(false)
  })
})
