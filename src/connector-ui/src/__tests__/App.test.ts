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

    const links = w.findAll('nav[aria-label="Workflow steps"] a')
    expect(links).toHaveLength(4)
    // Connect + Source Schema (idx 0,1) come before Export Schema (idx 2, active)
    expect(links[0]!.classes()).not.toContain('bg-nav-hover')
    expect(links[2]!.classes()).toContain('bg-nav-hover')
  })

  it('shows a checkmark for completed steps instead of the step number', async () => {
    const w = mount(App, { global: { plugins: [await buildRouter('/exports')] } })
    await flushPromises()

    const links = w.findAll('nav[aria-label="Workflow steps"] a')
    // Connect, Source Schema, Export Schema are all before Export (active) -> completed
    expect(links[0]!.find('svg').exists()).toBe(true)
    expect(links[0]!.text()).not.toContain('1')
    // The active step itself still shows its number
    expect(links[3]!.text()).toContain('4')
  })

  it('treats a route matching a step path prefix (export-detail) as the exports step being active', async () => {
    const w = mount(App, { global: { plugins: [await buildRouter('/exports/7')] } })
    await flushPromises()

    const links = w.findAll('nav[aria-label="Workflow steps"] a')
    expect(links[3]!.classes()).toContain('bg-nav-hover')
  })

  it('shows no active/completed step on a secondary page', async () => {
    const w = mount(App, { global: { plugins: [await buildRouter('/settings')] } })
    await flushPromises()

    const links = w.findAll('nav[aria-label="Workflow steps"] a')
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
    expect(secondary.text()).toContain('Export Definitions')
    expect(secondary.text()).toContain('Settings')
    expect(secondary.text()).toContain('Audit Log')
  })

  it('does not render the workflow nav when logged out', async () => {
    vi.spyOn(authApi, 'isLoggedIn').mockReturnValue(false)
    const w = mount(App, { global: { plugins: [await buildRouter('/login')] } })
    await flushPromises()

    expect(w.find('nav[aria-label="Workflow steps"]').exists()).toBe(false)
  })
})
