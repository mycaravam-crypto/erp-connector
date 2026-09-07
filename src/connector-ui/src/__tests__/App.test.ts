import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import App from '@/App.vue'
import * as authApi from '@/api/auth'

async function buildRouter(initialPath: string) {
  const r = createRouter({
    history: createMemoryHistory(),
    routes: [
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
  it('marks the step matching the current route as active and earlier steps as completed', async () => {
    const w = mount(App, { global: { plugins: [await buildRouter('/export-schema')] } })
    await flushPromises()

    const links = w.findAll('nav[aria-label="Connector"] a')
    expect(links).toHaveLength(4)
    // Connect + Source Schema (idx 0,1) come before CMDB Export Mapping (idx 2, active)
    expect(links[0]!.classes()).not.toContain('bg-nav-hover')
    expect(links[2]!.classes()).toContain('bg-nav-hover')
  })

  it('shows a checkmark for every setup step and highlights Managed Export once the connector is operating', async () => {
    const w = mount(App, { global: { plugins: [await buildRouter('/exports')] } })
    await flushPromises()

    const links = w.findAll('nav[aria-label="Connector"] a')
    // Connect, Source Schema, CMDB Export Mapping are all implicitly done once Managed Export is active
    expect(links[0]!.find('svg').exists()).toBe(true)
    expect(links[1]!.find('svg').exists()).toBe(true)
    expect(links[2]!.find('svg').exists()).toBe(true)
    // Managed Export itself is not a numbered/completable step — it's the active operational link
    expect(links[3]!.text()).toContain('Managed Export')
    expect(links[3]!.classes()).toContain('bg-nav-hover')
  })

  it('treats a route matching a step path prefix (export-detail) as the exports step being active', async () => {
    const w = mount(App, { global: { plugins: [await buildRouter('/exports/7')] } })
    await flushPromises()

    const links = w.findAll('nav[aria-label="Connector"] a')
    expect(links[3]!.classes()).toContain('bg-nav-hover')
  })

  it('shows no active/completed step on a secondary page', async () => {
    const w = mount(App, { global: { plugins: [await buildRouter('/settings')] } })
    await flushPromises()

    const links = w.findAll('nav[aria-label="Connector"] a')
    for (const link of links) {
      expect(link.classes()).not.toContain('bg-nav-hover')
      expect(link.find('svg').exists()).toBe(false)
    }
  })

  it('tucks the secondary nav inside the collapsed user menu until opened', async () => {
    const w = mount(App, { global: { plugins: [await buildRouter('/connect')] } })
    await flushPromises()

    expect(w.find('nav[aria-label="Secondary"]').exists()).toBe(false)

    await w.find('button[aria-haspopup="menu"]').trigger('click')

    const secondary = w.find('nav[aria-label="Secondary"]')
    expect(secondary.exists()).toBe(true)
    expect(secondary.text()).toContain('ICD Schema')
    expect(secondary.text()).toContain('Export Jobs')
    expect(secondary.text()).toContain('Settings')
    expect(secondary.text()).toContain('Audit Log')
  })

  it('does not render the connector nav when logged out', async () => {
    vi.spyOn(authApi, 'isLoggedIn').mockReturnValue(false)
    const w = mount(App, { global: { plugins: [await buildRouter('/login')] } })
    await flushPromises()

    expect(w.find('nav[aria-label="Connector"]').exists()).toBe(false)
  })
})
