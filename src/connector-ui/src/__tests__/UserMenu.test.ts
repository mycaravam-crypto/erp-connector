import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import UserMenu from '@/components/UserMenu.vue'

async function buildRouter() {
  const r = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/connect', name: 'connect', component: { template: '<div/>' } },
      { path: '/icd-schema', name: 'icd-schema', component: { template: '<div/>' } },
      { path: '/export-definitions', name: 'export-definitions', component: { template: '<div/>' } },
      { path: '/settings', name: 'settings', component: { template: '<div/>' } },
      { path: '/audit', name: 'audit', component: { template: '<div/>' } },
    ],
  })
  await r.push('/connect')
  return r
}

describe('UserMenu', () => {
  it('is closed by default and shows the username on the trigger', async () => {
    const w = mount(UserMenu, { props: { username: 'alice' }, global: { plugins: [await buildRouter()] } })

    expect(w.find('button[aria-haspopup="menu"]').text()).toContain('alice')
    expect(w.find('button[aria-haspopup="menu"]').attributes('aria-expanded')).toBe('false')
    expect(w.find('nav[aria-label="Secondary"]').exists()).toBe(false)
  })

  it('opens the panel on trigger click and closes it again on a second click', async () => {
    const w = mount(UserMenu, { props: { username: 'alice' }, global: { plugins: [await buildRouter()] } })
    const trigger = w.find('button[aria-haspopup="menu"]')

    await trigger.trigger('click')
    expect(trigger.attributes('aria-expanded')).toBe('true')
    expect(w.find('nav[aria-label="Secondary"]').exists()).toBe(true)

    await trigger.trigger('click')
    expect(trigger.attributes('aria-expanded')).toBe('false')
    expect(w.find('nav[aria-label="Secondary"]').exists()).toBe(false)
  })

  it('closes on Escape', async () => {
    const w = mount(UserMenu, { props: { username: 'alice' }, global: { plugins: [await buildRouter()] } })
    await w.find('button[aria-haspopup="menu"]').trigger('click')
    expect(w.find('nav[aria-label="Secondary"]').exists()).toBe(true)

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
    await w.vm.$nextTick()

    expect(w.find('nav[aria-label="Secondary"]').exists()).toBe(false)
  })

  it('closes on an outside click', async () => {
    const w = mount(UserMenu, {
      props: { username: 'alice' },
      global: { plugins: [await buildRouter()] },
      attachTo: document.body,
    })
    await w.find('button[aria-haspopup="menu"]').trigger('click')
    expect(w.find('nav[aria-label="Secondary"]').exists()).toBe(true)

    document.body.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    await w.vm.$nextTick()

    expect(w.find('nav[aria-label="Secondary"]').exists()).toBe(false)
    w.unmount()
  })

  it('emits signOut and closes when the sign out button is clicked', async () => {
    const w = mount(UserMenu, { props: { username: 'alice' }, global: { plugins: [await buildRouter()] } })
    await w.find('button[aria-haspopup="menu"]').trigger('click')

    const signOutButton = w.findAll('button').find((b) => b.text().includes('Sign out'))
    await signOutButton!.trigger('click')

    expect(w.emitted('signOut')).toHaveLength(1)
    expect(w.find('nav[aria-label="Secondary"]').exists()).toBe(false)
  })
})
