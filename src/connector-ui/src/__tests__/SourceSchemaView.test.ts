import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import SourceSchemaView from '@/views/SourceSchemaView.vue'
import * as connectionApi from '@/api/connection'
import type { SourceSchema } from '@/api/connection'

function buildRouter() {
  const r = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/source-schema', name: 'source-schema', component: SourceSchemaView },
      { path: '/connect',       name: 'connect',       component: { template: '<div/>' } },
      { path: '/export-schema', name: 'export-schema', component: { template: '<div/>' } },
    ],
  })
  r.push('/source-schema')
  return r
}

const SCHEMA: SourceSchema = {
  connectionLabel: 'db:5432/test',
  tables: [
    {
      name: 'parts',
      description: 'Parts table',
      columns: [
        { name: 'id',   type: 'uuid', nullable: false, primaryKey: true },
        { name: 'name', type: 'text', nullable: true,  primaryKey: false },
      ],
    },
    {
      name: 'orders',
      description: 'Orders table',
      columns: [
        { name: 'id', type: 'uuid', nullable: false, primaryKey: true },
      ],
    },
  ],
}

beforeEach(() => vi.restoreAllMocks())

describe('SourceSchemaView', () => {
  it('shows loading state initially', () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockReturnValue(new Promise(() => {}))
    const w = mount(SourceSchemaView, { global: { plugins: [buildRouter()] } })
    expect(w.text()).toContain('Reading schema')
  })

  it('shows error on API rejection', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockRejectedValueOnce(new Error('network'))
    const w = mount(SourceSchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('Could not reach')
  })

  it('shows error when getSourceSchema returns null', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(null)
    const w = mount(SourceSchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('no data')
  })

  it('shows all table names after load', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SourceSchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('parts')
    expect(w.text()).toContain('orders')
  })

  it('shows the connection label and table count chip', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SourceSchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('db:5432/test')
    expect(w.text()).toContain('2 tables')
  })

  it('tables start collapsed — no column rows visible', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SourceSchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    // Column names only appear in tbody when a table is expanded
    expect(w.find('tbody').exists()).toBe(false)
  })

  it('clicking a table header expands its columns', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SourceSchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    const tableBtn = w.findAll('button').find((b) => b.text().includes('parts'))!
    await tableBtn.trigger('click')
    expect(w.text()).toContain('name') // column name visible
    expect(w.find('tbody').exists()).toBe(true)
  })

  it('clicking an expanded table header collapses it', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SourceSchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    const tableBtn = w.findAll('button').find((b) => b.text().includes('parts'))!
    await tableBtn.trigger('click') // expand
    expect(w.find('tbody').exists()).toBe(true)
    await tableBtn.trigger('click') // collapse
    expect(w.find('tbody').exists()).toBe(false)
  })

  it('Expand All button expands every table', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SourceSchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    const expandBtn = w.findAll('button').find((b) => b.text() === 'Expand All')!
    await expandBtn.trigger('click')
    expect(w.findAll('tbody')).toHaveLength(SCHEMA.tables.length)
  })

  it('Expand All becomes Collapse All after all are expanded', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SourceSchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    const expandBtn = w.findAll('button').find((b) => b.text() === 'Expand All')!
    await expandBtn.trigger('click')
    expect(w.findAll('button').some((b) => b.text() === 'Collapse All')).toBe(true)
  })

  it('Collapse All collapses all expanded tables', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SourceSchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    // expand all first
    const expandBtn = w.findAll('button').find((b) => b.text() === 'Expand All')!
    await expandBtn.trigger('click')
    expect(w.findAll('tbody').length).toBeGreaterThan(0)

    // now collapse all
    const collapseBtn = w.findAll('button').find((b) => b.text() === 'Collapse All')!
    await collapseBtn.trigger('click')
    expect(w.find('tbody').exists()).toBe(false)
  })
})
