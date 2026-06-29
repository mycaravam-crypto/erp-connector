import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import SchemaView from '@/views/SchemaView.vue'
import * as connectionApi from '@/api/connection'
import * as erpApi from '@/api/erp'
import type { SourceSchema } from '@/api/connection'

function buildRouter() {
  const r = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/export-schema', name: 'export-schema', component: SchemaView }],
  })
  r.push('/export-schema')
  return r
}

const SCHEMA: SourceSchema = {
  connectionLabel: 'localhost:5432/testdb',
  tables: [
    {
      name: 'systemconfiguration',
      description: 'CI instances',
      columns: [
        { name: 'id',           type: 'uuid',                      nullable: false, primaryKey: true },
        { name: 'serial',       type: 'character varying(100)',     nullable: true,  primaryKey: false },
        { name: 'article_id',   type: 'uuid',                      nullable: true,  primaryKey: false },
        { name: 'status',       type: 'character varying(50)',      nullable: true,  primaryKey: false },
      ],
    },
    {
      name: 'masterdata',
      description: 'Article master',
      columns: [
        { name: 'id',           type: 'uuid',                      nullable: false, primaryKey: true },
        { name: 'article_name', type: 'character varying(200)',     nullable: true,  primaryKey: false },
      ],
    },
  ],
}

beforeEach(() => {
  vi.restoreAllMocks()
  vi.spyOn(erpApi, 'getExportMapping').mockResolvedValue(null)
  vi.spyOn(erpApi, 'saveExportMapping').mockResolvedValue({ ok: true })
})

describe('SchemaView', () => {
  it('shows loading initially', () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockReturnValue(new Promise(() => {}))
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    expect(w.text()).toContain('Loading')
  })

  it('shows error when source schema fetch throws', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockRejectedValueOnce(new Error('network'))
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('Could not reach')
  })

  it('shows error when getSourceSchema returns null', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(null)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('Configure a database connection')
  })

  it('shows table selector with available tables', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('systemconfiguration')
    expect(w.text()).toContain('masterdata')
  })

  it('shows connection label chip', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.find('.conn-chip').text()).toContain('localhost:5432/testdb')
  })

  it('shows column rows after a table is selected', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    const sel = w.find('select.table-select')
    await sel.setValue('systemconfiguration')
    await w.vm.$nextTick()

    const tbody = w.find('.col-table tbody')
    expect(tbody.findAll('tr')).toHaveLength(4)
    expect(tbody.text()).toContain('id')
    expect(tbody.text()).toContain('serial')
  })

  it('marks the PK column with PK badge', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.table-select').setValue('systemconfiguration')
    await w.vm.$nextTick()

    expect(w.find('.pk-badge').text()).toBe('PK')
  })

  it('shows Add Relation button after table is selected', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.table-select').setValue('systemconfiguration')
    await w.vm.$nextTick()

    expect(w.find('.add-btn').exists()).toBe(true)
  })

  it('adds a relation row when Add Relation is clicked', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.table-select').setValue('systemconfiguration')
    await w.vm.$nextTick()

    expect(w.findAll('.relation-card')).toHaveLength(0)
    await w.find('.add-btn').trigger('click')
    expect(w.findAll('.relation-card')).toHaveLength(1)
  })

  it('removes a relation row when × is clicked', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.table-select').setValue('systemconfiguration')
    await w.vm.$nextTick()

    await w.find('.add-btn').trigger('click')
    expect(w.findAll('.relation-card')).toHaveLength(1)
    await w.find('.rel-remove-btn').trigger('click')
    expect(w.findAll('.relation-card')).toHaveLength(0)
  })

  it('shows format buttons', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.table-select').setValue('systemconfiguration')
    await w.vm.$nextTick()

    const fmts = w.findAll('.format-btn')
    expect(fmts.length).toBeGreaterThanOrEqual(3)
    const labels = fmts.map((b) => b.text())
    expect(labels.some((l) => l.includes('xlsx'))).toBe(true)
    expect(labels.some((l) => l.includes('csv'))).toBe(true)
    expect(labels.some((l) => l.includes('json'))).toBe(true)
  })

  it('calls saveExportMapping when Save Mapping is clicked', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const saveSpy = vi.spyOn(erpApi, 'saveExportMapping').mockResolvedValue({ ok: true })
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.table-select').setValue('systemconfiguration')
    await w.vm.$nextTick()

    await w.find('.btn-save').trigger('click')
    await flushPromises()

    expect(saveSpy).toHaveBeenCalledOnce()
    const [config] = saveSpy.mock.calls[0]
    expect(config.sourceTable).toBe('systemconfiguration')
  })

  it('shows save-ok confirmation after successful save', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    vi.spyOn(erpApi, 'saveExportMapping').mockResolvedValue({ ok: true })
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.table-select').setValue('systemconfiguration')
    await w.vm.$nextTick()

    await w.find('.btn-save').trigger('click')
    await flushPromises()

    expect(w.find('.save-ok').exists()).toBe(true)
  })

  it('shows save-error when backend rejects the mapping', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    vi.spyOn(erpApi, 'saveExportMapping').mockResolvedValue({ ok: false, error: 'Bad request' })
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.table-select').setValue('systemconfiguration')
    await w.vm.$nextTick()

    await w.find('.btn-save').trigger('click')
    await flushPromises()

    expect(w.find('.save-error').exists()).toBe(true)
    expect(w.find('.save-error').text()).toContain('Bad request')
  })

  it('restores existing mapping on load', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    vi.spyOn(erpApi, 'getExportMapping').mockResolvedValueOnce({
      sourceTable: 'systemconfiguration',
      fields: [
        { sourceName: 'id', targetName: 'u_cmdb_guid', enabled: true },
        { sourceName: 'serial', targetName: 'serial_number', enabled: true },
        { sourceName: 'article_id', targetName: 'article_id', enabled: false },
        { sourceName: 'status', targetName: 'status', enabled: false },
      ],
      relations: [],
    })
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    expect(w.find('.col-table').exists()).toBe(true)
    const inputs = w.findAll('input.export-as-input')
    const values = inputs.map((i) => (i.element as HTMLInputElement).value)
    expect(values).toContain('u_cmdb_guid')
  })
})
