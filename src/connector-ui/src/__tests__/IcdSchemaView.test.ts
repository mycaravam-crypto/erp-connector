import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import IcdSchemaView from '@/views/IcdSchemaView.vue'
import * as erpApi from '@/api/icdSchema'

function buildRouter() {
  const r = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/icd-schema', name: 'icd-schema', component: IcdSchemaView }],
  })
  r.push('/icd-schema')
  return r
}

const SCHEMA = {
  version: '2.0',
  columns: [
    { name: 'guid',              erpSource: 'systemconfiguration.id',             type: 'UUID text',          notes: 'Coalesce key',                active: true,  exportName: null },
    { name: 'serial_number',     erpSource: 'systemconfiguration.serial',          type: 'Text (explicit)',     notes: 'Physical unit identity',       active: true,  exportName: null },
    { name: 'part_number',       erpSource: 'masterdata.part_number',              type: 'Text (explicit)',     notes: 'Model reference',               active: true,  exportName: null },
    { name: 'parent_serial_number', erpSource: 'articlestructure → sc.serial',    type: 'Text (explicit)',     notes: 'BOM parent reference',          active: true,  exportName: null },
    { name: 'model_reference',   erpSource: 'masterdata.article_name',             type: 'Text',               notes: 'Human-readable model name',    active: true,  exportName: null },
    { name: 'commissioning_date',erpSource: 'systemconfiguration.commission_date', type: 'ISO 8601 date',      notes: 'Warranty start date',          active: true,  exportName: null },
    { name: 'maintenance_state', erpSource: 'systemconfiguration.status',          type: 'Text (mapped enum)', notes: 'CI lifecycle state',            active: true,  exportName: null },
  ],
}

beforeEach(() => vi.restoreAllMocks())

describe('IcdSchemaView', () => {
  it('shows loading state initially', () => {
    vi.spyOn(erpApi, 'getSchema').mockReturnValue(new Promise(() => {}))
    const w = mount(IcdSchemaView, { global: { plugins: [buildRouter()] } })
    expect(w.text()).toContain('Loading schema')
  })

  it('shows error when API throws', async () => {
    vi.spyOn(erpApi, 'getSchema').mockRejectedValueOnce(new Error('network'))
    const w = mount(IcdSchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('Could not reach')
  })

  it('shows error when getSchema returns null', async () => {
    vi.spyOn(erpApi, 'getSchema').mockResolvedValueOnce(null)
    const w = mount(IcdSchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('No schema data')
  })

  it('shows schema version badge', async () => {
    vi.spyOn(erpApi, 'getSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(IcdSchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('v2.0')
  })

  it('renders all 7 ICD column names', async () => {
    vi.spyOn(erpApi, 'getSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(IcdSchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    for (const col of SCHEMA.columns)
      expect(w.text()).toContain(col.name)
  })

  it('renders ERP source fields', async () => {
    vi.spyOn(erpApi, 'getSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(IcdSchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('systemconfiguration.id')
    expect(w.text()).toContain('masterdata.part_number')
  })

  it('shows active column count chip', async () => {
    vi.spyOn(erpApi, 'getSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(IcdSchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('7 of 7')
  })

  it('shows coalesce key note', async () => {
    vi.spyOn(erpApi, 'getSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(IcdSchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('Coalesce key')
    expect(w.text()).toContain('guid')
  })

  it('shows excluded fields section with GDPR tag', async () => {
    vi.spyOn(erpApi, 'getSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(IcdSchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('technician_name')
    expect(w.text()).toContain('GDPR Art. 5(1)(c)')
  })

  it('shows excluded fields section with Open Point #4 tag', async () => {
    vi.spyOn(erpApi, 'getSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(IcdSchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('storagelocation.location_id')
    expect(w.text()).toContain('Open Point #4')
  })

  it('shows (off) indicator for inactive columns', async () => {
    const schemaWithOff = {
      ...SCHEMA,
      columns: [
        ...SCHEMA.columns.slice(0, 6),
        { ...SCHEMA.columns[6], active: false },
      ],
    }
    vi.spyOn(erpApi, 'getSchema').mockResolvedValueOnce(schemaWithOff)
    const w = mount(IcdSchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('(off)')
    expect(w.text()).toContain('6 of 7')
  })
})
