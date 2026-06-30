import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import ConnectionView from '@/views/ConnectionView.vue'
import * as connectionApi from '@/api/connection'
import * as authApi from '@/api/auth'
import type { ErpConnectionInfo, SourceSchema } from '@/api/connection'

function buildRouter() {
  const r = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/connect',       name: 'connect',       component: ConnectionView },
      { path: '/source-schema', name: 'source-schema', component: { template: '<div/>' } },
      { path: '/login',         name: 'login',         component: { template: '<div/>' } },
    ],
  })
  r.push('/connect')
  return r
}

const STORED_CONNECTION: ErpConnectionInfo = {
  host: 'db.example.com',
  port: 5432,
  database: 'erp_prod',
  username: 'readonly',
}

const SCHEMA: SourceSchema = {
  connectionLabel: 'db.example.com:5432/erp_prod',
  tables: [{ name: 'items', description: '', columns: [] }, { name: 'orders', description: '', columns: [] }],
}

beforeEach(() => {
  vi.restoreAllMocks()
  vi.spyOn(connectionApi, 'getConnection').mockResolvedValue(null)
})

describe('ConnectionView', () => {
  it('renders all form fields', async () => {
    const w = mount(ConnectionView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.find('#host').exists()).toBe(true)
    expect(w.find('#port').exists()).toBe(true)
    expect(w.find('#database').exists()).toBe(true)
    expect(w.find('#username').exists()).toBe(true)
    expect(w.find('#password').exists()).toBe(true)
  })

  it('shows "No connection configured yet" banner when no connection is stored', async () => {
    const w = mount(ConnectionView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('No connection configured yet')
  })

  it('shows "Connected:" chip when a connection is stored', async () => {
    vi.spyOn(connectionApi, 'getConnection').mockResolvedValue(STORED_CONNECTION)
    const w = mount(ConnectionView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('Connected:')
    expect(w.text()).toContain('db.example.com:5432/erp_prod')
  })

  it('pre-fills form fields from stored connection', async () => {
    vi.spyOn(connectionApi, 'getConnection').mockResolvedValue(STORED_CONNECTION)
    const w = mount(ConnectionView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect((w.find('#host').element as HTMLInputElement).value).toBe('db.example.com')
    expect((w.find('#port').element as HTMLInputElement).value).toBe('5432')
    expect((w.find('#database').element as HTMLInputElement).value).toBe('erp_prod')
    expect((w.find('#username').element as HTMLInputElement).value).toBe('readonly')
  })

  it('host field starts empty when no connection is stored', async () => {
    const w = mount(ConnectionView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect((w.find('#host').element as HTMLInputElement).value).toBe('')
  })

  it('shows port validation error for out-of-range value', async () => {
    const w = mount(ConnectionView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    await w.find('#port').setValue('99999')
    expect(w.text()).toContain('Port must be a number between 1 and 65535')
  })

  it('shows port validation error for non-numeric value', async () => {
    const w = mount(ConnectionView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    await w.find('#port').setValue('abc')
    expect(w.text()).toContain('Port must be a number between 1 and 65535')
  })

  it('blocks submission and shows error when port is invalid', async () => {
    const spy = vi.spyOn(connectionApi, 'saveConnection')
    const w = mount(ConnectionView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    await w.find('#port').setValue('0')
    await w.find('form').trigger('submit')
    await flushPromises()
    expect(spy).not.toHaveBeenCalled()
    expect(w.text()).toContain('Port must be a number between 1 and 65535')
  })

  it('calls saveConnection with correct args on submit', async () => {
    vi.spyOn(connectionApi, 'saveConnection').mockResolvedValue({ schema: SCHEMA })
    const w = mount(ConnectionView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    await w.find('#host').setValue('myhost')
    await w.find('#port').setValue('5433')
    await w.find('#database').setValue('mydb')
    await w.find('#username').setValue('user1')
    await w.find('#password').setValue('s3cr3t')
    await w.find('form').trigger('submit')
    await flushPromises()
    expect(connectionApi.saveConnection).toHaveBeenCalledWith({
      host: 'myhost',
      port: 5433,
      database: 'mydb',
      username: 'user1',
      password: 's3cr3t',
    })
  })

  it('shows success message and connected chip after successful connection test', async () => {
    vi.spyOn(connectionApi, 'saveConnection').mockResolvedValue({ schema: SCHEMA })
    const w = mount(ConnectionView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    await w.find('#host').setValue('myhost')
    await w.find('#port').setValue('5432')
    await w.find('#database').setValue('mydb')
    await w.find('#username').setValue('user1')
    await w.find('#password').setValue('pw')
    await w.find('form').trigger('submit')
    await flushPromises()
    expect(w.text()).toContain('Connected')
    expect(w.text()).toContain('2 tables')
  })

  it('shows error message when saveConnection returns an error', async () => {
    vi.spyOn(connectionApi, 'saveConnection').mockResolvedValue({
      error: 'password authentication failed',
      status: 400,
    })
    const w = mount(ConnectionView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    await w.find('#host').setValue('myhost')
    await w.find('#port').setValue('5432')
    await w.find('#database').setValue('mydb')
    await w.find('#username').setValue('user1')
    await w.find('#password').setValue('wrong')
    await w.find('form').trigger('submit')
    await flushPromises()
    expect(w.text()).toContain('password authentication failed')
  })

  it('shows network error message when saveConnection throws', async () => {
    vi.spyOn(connectionApi, 'saveConnection').mockRejectedValue(new Error('fetch failed'))
    const w = mount(ConnectionView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    await w.find('#host').setValue('myhost')
    await w.find('#port').setValue('5432')
    await w.find('#database').setValue('mydb')
    await w.find('#username').setValue('user1')
    await w.find('#password').setValue('pw')
    await w.find('form').trigger('submit')
    await flushPromises()
    expect(w.text()).toContain('Is the backend service running')
  })

  it('redirects to login when saveConnection returns 401', async () => {
    vi.spyOn(connectionApi, 'saveConnection').mockResolvedValue({ error: 'Unauthorized', status: 401 })
    vi.spyOn(authApi, 'clearSession').mockReturnValue(undefined)
    const router = buildRouter()
    const w = mount(ConnectionView, { global: { plugins: [router] } })
    await flushPromises()
    await w.find('#host').setValue('myhost')
    await w.find('#port').setValue('5432')
    await w.find('#database').setValue('mydb')
    await w.find('#username').setValue('user1')
    await w.find('#password').setValue('pw')
    await w.find('form').trigger('submit')
    await flushPromises()
    expect(authApi.clearSession).toHaveBeenCalled()
    expect(router.currentRoute.value.name).toBe('login')
  })

  it('shows "Testing…" label while request is in-flight', async () => {
    let resolve!: (v: { schema: SourceSchema }) => void
    vi.spyOn(connectionApi, 'saveConnection').mockReturnValue(
      new Promise((r) => { resolve = r }),
    )
    const w = mount(ConnectionView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    await w.find('#host').setValue('myhost')
    await w.find('#port').setValue('5432')
    await w.find('#database').setValue('mydb')
    await w.find('#username').setValue('user1')
    await w.find('#password').setValue('pw')
    await w.find('form').trigger('submit')
    await w.vm.$nextTick()
    expect(w.find('button[type="submit"]').text()).toContain('Testing')
    resolve({ schema: SCHEMA })
  })
})
