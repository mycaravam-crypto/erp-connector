import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import SchemaView from '@/views/SchemaView.vue'
import * as erpApi from '@/api/erp'
import type { SchemaDefinition } from '@/api/erp'

function buildRouter() {
  const r = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/schema', name: 'schema', component: SchemaView }],
  })
  r.push('/schema')
  return r
}

beforeEach(() => vi.restoreAllMocks())

const SCHEMA: SchemaDefinition = {
  version: '2.0',
  columns: [
    { name: 'guid', erpSource: 'systemconfiguration.id', type: 'UUID text', notes: 'Coalesce key', active: true },
    { name: 'serial_number', erpSource: 'systemconfiguration.serial', type: 'Text (explicit)', notes: 'Physical identity', active: true },
    { name: 'part_number', erpSource: 'masterdata.part_number', type: 'Text (explicit)', notes: 'Model reference', active: true },
    { name: 'parent_serial_number', erpSource: 'articlestructure → systemconfiguration.serial', type: 'Text (explicit)', notes: 'BOM parent', active: true },
    { name: 'model_reference', erpSource: 'masterdata.article_name', type: 'Text', notes: 'Model name', active: true },
    { name: 'commissioning_date', erpSource: 'systemconfiguration.commission_date', type: 'ISO 8601 date', notes: 'Warranty start', active: true },
    { name: 'maintenance_state', erpSource: 'systemconfiguration.status', type: 'Text (mapped enum)', notes: 'CI state', active: true },
  ],
}

describe('SchemaView', () => {
  it('shows loading initially', () => {
    vi.spyOn(erpApi, 'getSchema').mockReturnValue(new Promise(() => {}))
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    expect(w.text()).toContain('Loading')
  })

  it('shows error when fetch throws', async () => {
    vi.spyOn(erpApi, 'getSchema').mockRejectedValueOnce(new Error('network'))
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('Could not reach')
  })

  it('shows error when getSchema returns null', async () => {
    vi.spyOn(erpApi, 'getSchema').mockResolvedValueOnce(null)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('no data')
  })

  it('renders schema version badge', async () => {
    vi.spyOn(erpApi, 'getSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.find('.version-badge').text()).toBe('v2.0')
  })

  it('renders one row per column', async () => {
    vi.spyOn(erpApi, 'getSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    // first table is the active columns table; filter only its tbody rows
    const firstTbody = w.find('table tbody')
    expect(firstTbody.findAll('tr')).toHaveLength(7)
  })

  it('shows column names in code elements', async () => {
    vi.spyOn(erpApi, 'getSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('guid')
    expect(w.text()).toContain('serial_number')
    expect(w.text()).toContain('commissioning_date')
  })

  it('shows pending section with Open Point #4 and GDPR entries', async () => {
    vi.spyOn(erpApi, 'getSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('Open Point #4')
    expect(w.text()).toContain('GDPR')
    expect(w.text()).toContain('storagelocation.location_id')
    expect(w.text()).toContain('technician_name')
  })

  it('shows the coalesce key explanation', async () => {
    vi.spyOn(erpApi, 'getSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('Coalesce Key')
    expect(w.text()).toContain('coalesce')
  })
})
