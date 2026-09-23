import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import ConnectionView from '@/views/ConnectionView.vue'
import * as connectionApi from '@/api/connection'
import * as authApi from '@/api/auth'
import { DataSourceType, type ErpConnectionInfo, type SourceSchema } from '@/api/connection'
import { useToasts } from '@/composables/useToasts'

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
  sslMode: null,
}

const SCHEMA: SourceSchema = {
  connectionLabel: 'db.example.com:5432/erp_prod',
  tables: [{ name: 'items', description: '', columns: [] }, { name: 'orders', description: '', columns: [] }],
}

beforeEach(() => {
  vi.restoreAllMocks()
  vi.spyOn(connectionApi, 'getConnection').mockResolvedValue(null)
  useToasts().clear()
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
    expect(w.find('#ssl-mode').exists()).toBe(true)
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

  it('pre-fills SSL Mode from stored connection', async () => {
    vi.spyOn(connectionApi, 'getConnection').mockResolvedValue({ ...STORED_CONNECTION, sslMode: 'VerifyFull' })
    const w = mount(ConnectionView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect((w.find('#ssl-mode').element as HTMLSelectElement).value).toBe('VerifyFull')
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
      type: DataSourceType.PostgreSql,
      host: 'myhost',
      port: 5433,
      database: 'mydb',
      instanceUrl: null,
      username: 'user1',
      password: 's3cr3t',
      sslMode: '',
    })
  })

  it('includes the chosen SSL Mode when submitting', async () => {
    vi.spyOn(connectionApi, 'saveConnection').mockResolvedValue({ schema: SCHEMA })
    const w = mount(ConnectionView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    await w.find('#host').setValue('myhost')
    await w.find('#port').setValue('5433')
    await w.find('#database').setValue('mydb')
    await w.find('#username').setValue('user1')
    await w.find('#password').setValue('s3cr3t')
    await w.find('#ssl-mode').setValue('VerifyFull')
    await w.find('form').trigger('submit')
    await flushPromises()
    expect(connectionApi.saveConnection).toHaveBeenCalledWith(
      expect.objectContaining({ sslMode: 'VerifyFull' }),
    )
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
    expect(useToasts().toasts.value.some((t) => t.variant === 'success')).toBe(true)
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
    const toast = useToasts().toasts.value.find((t) => t.message === 'password authentication failed')
    expect(toast?.variant).toBe('danger')
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

async function mountView() {
  const w = mount(ConnectionView, { global: { plugins: [buildRouter()] } })
  await flushPromises()
  return w
}

describe('ConnectionView — source types (Arbeitsauftrag 8)', () => {
  it('defaults to PostgreSQL with port 5432', async () => {
    const w = await mountView()
    expect((w.find('#source-type').element as HTMLSelectElement).value).toBe('postgres')
    expect((w.find('#port').element as HTMLInputElement).value).toBe('5432')
  })

  it('switching to MariaDB sets port 3306 and back to PostgreSQL restores 5432', async () => {
    const w = await mountView()
    await w.find('#source-type').setValue('mariadb')
    expect((w.find('#port').element as HTMLInputElement).value).toBe('3306')
    expect(w.text()).toContain('TLS Mode')
    await w.find('#source-type').setValue('postgres')
    expect((w.find('#port').element as HTMLInputElement).value).toBe('5432')
  })

  it('keeps a custom port when switching provider', async () => {
    const w = await mountView()
    await w.find('#port').setValue('6000')
    await w.find('#source-type').setValue('mariadb')
    expect((w.find('#port').element as HTMLInputElement).value).toBe('6000')
  })

  it('ServiceNow shows instance URL and access method instead of host/port/database/TLS', async () => {
    const w = await mountView()
    await w.find('#source-type').setValue('servicenow')
    expect(w.find('#instance-url').exists()).toBe(true)
    expect(w.find('#access-method').exists()).toBe(true)
    expect(w.find('#host').exists()).toBe(false)
    expect(w.find('#port').exists()).toBe(false)
    expect(w.find('#database').exists()).toBe(false)
    expect(w.find('#ssl-mode').exists()).toBe(false)
  })

  it('submits a MariaDB config with its type', async () => {
    vi.spyOn(connectionApi, 'saveConnection').mockResolvedValue({ schema: SCHEMA })
    const w = await mountView()
    await w.find('#source-type').setValue('mariadb')
    await w.find('#host').setValue('maria')
    await w.find('#database').setValue('erp')
    await w.find('#username').setValue('reader')
    await w.find('#password').setValue('pw')
    await w.find('#ssl-mode').setValue('Require')
    await w.find('form').trigger('submit')
    await flushPromises()
    expect(connectionApi.saveConnection).toHaveBeenCalledWith({
      type: DataSourceType.MariaDb,
      host: 'maria',
      port: 3306,
      database: 'erp',
      instanceUrl: null,
      username: 'reader',
      password: 'pw',
      sslMode: 'Require',
    })
  })

  it('submits a ServiceNow config with the chosen access method and no relational fields', async () => {
    vi.spyOn(connectionApi, 'saveConnection').mockResolvedValue({ schema: SCHEMA })
    const w = await mountView()
    await w.find('#source-type').setValue('servicenow')
    await w.find('#instance-url').setValue('https://acme.service-now.com')
    await w.find('#access-method').setValue('sql')
    await w.find('#username').setValue('svc')
    await w.find('#password').setValue('pw')
    await w.find('form').trigger('submit')
    await flushPromises()
    expect(connectionApi.saveConnection).toHaveBeenCalledWith({
      type: DataSourceType.ServiceNowSqlApi,
      host: null,
      port: null,
      database: null,
      instanceUrl: 'https://acme.service-now.com',
      username: 'svc',
      password: 'pw',
      sslMode: null,
    })
  })

  it('blocks submission and names every missing required field (relational)', async () => {
    const spy = vi.spyOn(connectionApi, 'saveConnection')
    const w = await mountView()
    await w.find('form').trigger('submit')
    await flushPromises()
    expect(spy).not.toHaveBeenCalled()
    expect(w.text()).toContain('Host is required.')
    expect(w.text()).toContain('Database is required.')
    expect(w.text()).toContain('Username is required.')
  })

  it('requires an https instance URL for ServiceNow', async () => {
    const spy = vi.spyOn(connectionApi, 'saveConnection')
    const w = await mountView()
    await w.find('#source-type').setValue('servicenow')
    await w.find('#instance-url').setValue('http://acme.service-now.com')
    await w.find('#username').setValue('svc')
    await w.find('form').trigger('submit')
    await flushPromises()
    expect(spy).not.toHaveBeenCalled()
    expect(w.text()).toContain('Instance URL must start with https://')
  })

  it('loads a stored config without a type as PostgreSQL', async () => {
    vi.spyOn(connectionApi, 'getConnection').mockResolvedValue(STORED_CONNECTION)
    const w = await mountView()
    expect((w.find('#source-type').element as HTMLSelectElement).value).toBe('postgres')
    expect((w.find('#host').element as HTMLInputElement).value).toBe('db.example.com')
  })

  it('loads a stored MariaDB config', async () => {
    vi.spyOn(connectionApi, 'getConnection').mockResolvedValue({
      ...STORED_CONNECTION,
      type: DataSourceType.MariaDb,
      port: 3306,
    })
    const w = await mountView()
    expect((w.find('#source-type').element as HTMLSelectElement).value).toBe('mariadb')
    expect((w.find('#port').element as HTMLInputElement).value).toBe('3306')
  })

  it('loads a stored ServiceNow SQL API config', async () => {
    vi.spyOn(connectionApi, 'getConnection').mockResolvedValue({
      type: DataSourceType.ServiceNowSqlApi,
      host: null,
      port: null,
      database: null,
      instanceUrl: 'https://acme.service-now.com',
      username: 'svc',
      sslMode: null,
      hasPassword: true,
    })
    const w = await mountView()
    expect((w.find('#source-type').element as HTMLSelectElement).value).toBe('servicenow')
    expect((w.find('#access-method').element as HTMLSelectElement).value).toBe('sql')
    expect((w.find('#instance-url').element as HTMLInputElement).value).toBe('https://acme.service-now.com')
    expect(w.text()).toContain('https://acme.service-now.com')
  })

  it('never fills the password from the stored config and signals a stored one via the placeholder', async () => {
    vi.spyOn(connectionApi, 'getConnection').mockResolvedValue({ ...STORED_CONNECTION, hasPassword: true })
    const w = await mountView()
    const input = w.find('#password').element as HTMLInputElement
    expect(input.value).toBe('')
    expect(input.placeholder).toContain('leave empty to keep')
  })

  it('submits an empty password to keep the stored one', async () => {
    vi.spyOn(connectionApi, 'getConnection').mockResolvedValue({ ...STORED_CONNECTION, hasPassword: true })
    vi.spyOn(connectionApi, 'saveConnection').mockResolvedValue({ schema: SCHEMA })
    const w = await mountView()
    await w.find('form').trigger('submit')
    await flushPromises()
    expect(connectionApi.saveConnection).toHaveBeenCalledWith(expect.objectContaining({ password: '' }))
  })

  it('shows the API error message for a failed provider connection', async () => {
    vi.spyOn(connectionApi, 'saveConnection').mockResolvedValue({
      error: "Connection failed: Access denied for user 'reader'",
      status: 400,
    })
    const w = await mountView()
    await w.find('#source-type').setValue('mariadb')
    await w.find('#host').setValue('maria')
    await w.find('#database').setValue('erp')
    await w.find('#username').setValue('reader')
    await w.find('form').trigger('submit')
    await flushPromises()
    expect(w.text()).toContain("Access denied for user 'reader'")
  })
})
