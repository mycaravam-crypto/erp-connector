import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import SchemaView from '@/views/SchemaView.vue'
import * as erpApi from '@/api/erp'
import type { SchemaDefinition } from '@/api/erp'

function buildRouter() {
  const r = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/export-schema', name: 'export-schema', component: SchemaView }],
  })
  r.push('/export-schema')
  return r
}

// patchSchemaColumns is called on every toggle; mock it globally so tests don't hit the network.
beforeEach(() => {
  vi.restoreAllMocks()
  vi.spyOn(erpApi, 'patchSchemaColumns').mockResolvedValue([])
})

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

  it('renders schema version in header chip', async () => {
    vi.spyOn(erpApi, 'getSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.find('.version-chip').text()).toContain('v2.0')
  })

  it('renders one row per column', async () => {
    vi.spyOn(erpApi, 'getSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
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

  it('shows excluded fields section with Open Point #4 and GDPR entries', async () => {
    vi.spyOn(erpApi, 'getSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('Open Point #4')
    expect(w.text()).toContain('GDPR')
    expect(w.text()).toContain('systemconfiguration.storage_location')
    expect(w.text()).toContain('technician_name')
  })

  it('shows format selection buttons', async () => {
    vi.spyOn(erpApi, 'getSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    const fmts = w.findAll('.format-btn')
    expect(fmts.length).toBeGreaterThanOrEqual(3)
    const labels = fmts.map((b) => b.text())
    expect(labels.some((l) => l.includes('xlsx'))).toBe(true)
    expect(labels.some((l) => l.includes('csv'))).toBe(true)
    expect(labels.some((l) => l.includes('json'))).toBe(true)
  })

  it('toggles a column off when checkbox is unchecked', async () => {
    vi.spyOn(erpApi, 'getSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    const checkboxes = w.findAll('input[type="checkbox"]')
    expect(checkboxes.length).toBe(7)
    const chip = w.find('.active-chip')
    expect(chip.text()).toContain('7 / 7')
    await checkboxes[0].setValue(false)
    expect(w.find('.active-chip').text()).toContain('6 / 7')
  })

  it('calls patchSchemaColumns when a column is toggled', async () => {
    vi.spyOn(erpApi, 'getSchema').mockResolvedValueOnce(SCHEMA)
    const patchSpy = vi.spyOn(erpApi, 'patchSchemaColumns').mockResolvedValue([])
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    await w.findAll('input[type="checkbox"]')[0].setValue(false)
    await flushPromises()
    expect(patchSpy).toHaveBeenCalled()
    // Should not include 'guid' since we unchecked it
    const [cols] = patchSpy.mock.calls[0]
    expect(cols).not.toContain('guid')
  })
})
