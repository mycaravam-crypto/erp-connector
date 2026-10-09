import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import ConnectorNav from '@/components/ConnectorNav.vue'
import { signInAs } from './signInAs'

async function buildRouter(initialPath = '/connect') {
  const r = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/connect', name: 'connect', component: { template: '<div/>' } },
      { path: '/source-schema', name: 'source-schema', component: { template: '<div/>' } },
      { path: '/export-schema', name: 'export-schema', component: { template: '<div/>' } },
      { path: '/exports', name: 'exports', component: { template: '<div/>' } },
      { path: '/export-definitions', name: 'export-definitions', component: { template: '<div/>' } },
      { path: '/import-definitions', name: 'import-definitions', component: { template: '<div/>' } },
    ],
  })
  await r.push(initialPath)
  return r
}

describe('ConnectorNav', () => {
  it('is closed by default — only the (desktop) pill row is in the DOM', async () => {
    const w = mount(ConnectorNav, { global: { plugins: [await buildRouter()] } })

    const toggle = w.find('button[aria-haspopup="menu"]')
    expect(toggle.attributes('aria-expanded')).toBe('false')
    expect(w.findAll('nav[aria-label="Connector"]')).toHaveLength(1)
  })

  it('opens the mobile dropdown panel on toggle click and closes it again on a second click', async () => {
    const w = mount(ConnectorNav, { global: { plugins: [await buildRouter()] } })
    const toggle = w.find('button[aria-haspopup="menu"]')

    await toggle.trigger('click')
    expect(toggle.attributes('aria-expanded')).toBe('true')
    // Desktop pill row (hidden via CSS at this width) plus the mobile dropdown panel.
    expect(w.findAll('nav[aria-label="Connector"]')).toHaveLength(2)

    await toggle.trigger('click')
    expect(toggle.attributes('aria-expanded')).toBe('false')
    expect(w.findAll('nav[aria-label="Connector"]')).toHaveLength(1)
  })

  it('closes on Escape', async () => {
    const w = mount(ConnectorNav, { global: { plugins: [await buildRouter()] } })
    await w.find('button[aria-haspopup="menu"]').trigger('click')
    expect(w.findAll('nav[aria-label="Connector"]')).toHaveLength(2)

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
    await w.vm.$nextTick()

    expect(w.findAll('nav[aria-label="Connector"]')).toHaveLength(1)
  })

  it('closes on an outside click', async () => {
    const w = mount(ConnectorNav, { global: { plugins: [await buildRouter()] }, attachTo: document.body })
    await w.find('button[aria-haspopup="menu"]').trigger('click')
    expect(w.findAll('nav[aria-label="Connector"]')).toHaveLength(2)

    document.body.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    await w.vm.$nextTick()

    expect(w.findAll('nav[aria-label="Connector"]')).toHaveLength(1)
    w.unmount()
  })

  it('closes when the route changes', async () => {
    const router = await buildRouter()
    const w = mount(ConnectorNav, { global: { plugins: [router] } })
    await w.find('button[aria-haspopup="menu"]').trigger('click')
    expect(w.findAll('nav[aria-label="Connector"]')).toHaveLength(2)

    await router.push({ name: 'exports' })
    await w.vm.$nextTick()

    expect(w.findAll('nav[aria-label="Connector"]')).toHaveLength(1)
  })
})

describe('ConnectorNav — permissions', () => {
  const stub = { template: '<div/>' }
  async function routerWithAccessRules() {
    const r = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', name: 'dashboard', component: stub },
        { path: '/connect', name: 'connect', component: stub, meta: { access: { admin: true } } },
        { path: '/source-schema', name: 'source-schema', component: stub, meta: { access: { anyOf: ['sourceSchema.view' as const] } } },
        { path: '/export-schema', name: 'export-schema', component: stub, meta: { access: { anyOf: ['exportMapping.view' as const] } } },
        { path: '/exports', name: 'exports', component: stub, meta: { access: { anyOf: ['managedExport.view' as const] } } },
        { path: '/export-definitions', name: 'export-definitions', component: stub, meta: { access: { anyOf: ['exportJobs.view' as const] } } },
        { path: '/import-definitions', name: 'import-definitions', component: stub, meta: { access: { anyOf: ['importJobs.view' as const] } } },
      ],
    })
    await r.push('/')
    return r
  }

  it('shows a User only the menu items they may open, without a dangling divider', async () => {
    await signInAs('User', ['exportJobs.view', 'importJobs.view'])
    const w = mount(ConnectorNav, { global: { plugins: [await routerWithAccessRules()] } })

    const nav = w.find('nav[aria-label="Connector"]')
    expect(nav.text()).toContain('Export Jobs')
    expect(nav.text()).toContain('Import Jobs')
    expect(nav.text()).not.toContain('Connect')
    expect(nav.text()).not.toContain('Managed Export')
    expect(nav.findAll('span.w-px')).toHaveLength(0)
  })

  it('shows an Admin every menu item with the divider between the two groups', async () => {
    await signInAs('Admin')
    const w = mount(ConnectorNav, { global: { plugins: [await routerWithAccessRules()] } })

    const nav = w.find('nav[aria-label="Connector"]')
    expect(nav.findAll('a')).toHaveLength(6)
    expect(nav.findAll('span.w-px')).toHaveLength(1)
  })
})
