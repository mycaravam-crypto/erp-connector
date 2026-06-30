import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import LoginView from '@/views/LoginView.vue'
import * as authApi from '@/api/auth'

function buildRouter() {
  const r = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/login',   name: 'login',   component: LoginView },
      { path: '/connect', name: 'connect', component: { template: '<div/>' } },
    ],
  })
  r.push('/login')
  return r
}

beforeEach(() => vi.restoreAllMocks())

describe('LoginView', () => {
  it('renders username and password inputs', () => {
    const w = mount(LoginView, { global: { plugins: [buildRouter()] } })
    expect(w.find('#username').exists()).toBe(true)
    expect(w.find('#password').exists()).toBe(true)
  })

  it('submit button is disabled when both fields are empty', () => {
    const w = mount(LoginView, { global: { plugins: [buildRouter()] } })
    expect(w.find('button').attributes('disabled')).toBeDefined()
  })

  it('submit button is disabled when only username is filled', async () => {
    const w = mount(LoginView, { global: { plugins: [buildRouter()] } })
    await w.find('#username').setValue('alice')
    expect(w.find('button').attributes('disabled')).toBeDefined()
  })

  it('submit button is enabled when both fields are filled', async () => {
    const w = mount(LoginView, { global: { plugins: [buildRouter()] } })
    await w.find('#username').setValue('alice')
    await w.find('#password').setValue('secret')
    expect(w.find('button').attributes('disabled')).toBeUndefined()
  })

  it('calls login with the entered credentials on button click', async () => {
    vi.spyOn(authApi, 'login').mockResolvedValueOnce({ ok: true })
    const w = mount(LoginView, { global: { plugins: [buildRouter()] } })
    await w.find('#username').setValue('alice')
    await w.find('#password').setValue('secret')
    await w.find('button').trigger('click')
    await flushPromises()
    expect(authApi.login).toHaveBeenCalledWith('alice', 'secret')
  })

  it('redirects to /connect (Step 1) on successful login', async () => {
    vi.spyOn(authApi, 'login').mockResolvedValueOnce({ ok: true })
    const router = buildRouter()
    const w = mount(LoginView, { global: { plugins: [router] } })
    await w.find('#username').setValue('alice')
    await w.find('#password').setValue('secret')
    await w.find('button').trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.name).toBe('connect')
  })

  it('shows error message on login failure', async () => {
    vi.spyOn(authApi, 'login').mockResolvedValueOnce({ ok: false, error: 'Invalid credentials.' })
    const w = mount(LoginView, { global: { plugins: [buildRouter()] } })
    await w.find('#username').setValue('alice')
    await w.find('#password').setValue('wrong')
    await w.find('button').trigger('click')
    await flushPromises()
    expect(w.text()).toContain('Invalid credentials.')
  })

  it('shows "Signing in…" label while the request is in-flight', async () => {
    let resolve!: (v: { ok: boolean }) => void
    vi.spyOn(authApi, 'login').mockReturnValueOnce(new Promise((r) => { resolve = r }))
    const w = mount(LoginView, { global: { plugins: [buildRouter()] } })
    await w.find('#username').setValue('alice')
    await w.find('#password').setValue('pw')
    await w.find('button').trigger('click')
    await w.vm.$nextTick()
    expect(w.find('button').text()).toContain('Signing in')
    resolve({ ok: true })
  })

  it('trims leading/trailing whitespace from username before calling login', async () => {
    vi.spyOn(authApi, 'login').mockResolvedValueOnce({ ok: true })
    const w = mount(LoginView, { global: { plugins: [buildRouter()] } })
    await w.find('#username').setValue('  alice  ')
    await w.find('#password').setValue('pw')
    await w.find('button').trigger('click')
    await flushPromises()
    expect(authApi.login).toHaveBeenCalledWith('alice', 'pw')
  })
})
