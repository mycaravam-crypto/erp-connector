import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import ErpDatabaseView from '@/views/ErpDatabaseView.vue'
import * as erpApi from '@/api/erp'
import type { ErpCiRecord } from '@/api/erp'

function buildRouter() {
  const r = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/erp', name: 'erp-database', component: ErpDatabaseView }],
  })
  r.push('/erp')
  return r
}

beforeEach(() => vi.restoreAllMocks())

const RACK: ErpCiRecord = {
  id: 'sc-rack-0001', serial: 'SN-RACK-0001', status: 'Active',
  commissionDate: '2023-03-01', articleName: 'Industrial Rack System',
  partNumber: 'P-RACK-42U', manufacturer: 'TechCorp GmbH',
  maintenancePlanStatus: 'Active', allocationChartRef: 'ALLOC-2023-V1',
  parentId: null, parentSerial: null,
  inScope: true, exclusionReason: null,
  technicianName: 'Klaus Bauer', storageLocation: 'Halle A, Reihe 3',
}

const BLADE: ErpCiRecord = {
  id: 'sc-blade-0001', serial: 'SN-BLD-0001', status: 'Active',
  commissionDate: '2023-03-15', articleName: 'Compute Module MK2',
  partNumber: 'P-BLADE-CM2', manufacturer: 'TechCorp GmbH',
  maintenancePlanStatus: 'Active', allocationChartRef: 'ALLOC-2023-V1',
  parentId: 'sc-rack-0001', parentSerial: 'SN-RACK-0001',
  inScope: true, exclusionReason: null,
  technicianName: 'Klaus Bauer', storageLocation: 'Halle A, Slot 1',
}

const EXCLUDED_PSU: ErpCiRecord = {
  id: 'sc-psu-0002', serial: 'SN-PSU-0002', status: 'Active',
  commissionDate: '2023-02-28', articleName: 'Power Supply 2400W',
  partNumber: 'P-PSU-2400', manufacturer: 'PowerTech AG',
  maintenancePlanStatus: null, allocationChartRef: null,
  parentId: 'sc-rack-0001', parentSerial: 'SN-RACK-0001',
  inScope: false, exclusionReason: 'No maintenance plan',
  technicianName: 'Anna Fischer', storageLocation: 'Halle A, PSU-Bay 2',
}

describe('ErpDatabaseView', () => {
  it('shows loading initially', () => {
    vi.spyOn(erpApi, 'listErpRecords').mockReturnValue(new Promise(() => {}))
    const w = mount(ErpDatabaseView, { global: { plugins: [buildRouter()] } })
    expect(w.text()).toContain('Loading')
  })

  it('shows error message when fetch throws', async () => {
    vi.spyOn(erpApi, 'listErpRecords').mockRejectedValueOnce(new Error('network'))
    const w = mount(ErpDatabaseView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('Could not reach')
  })

  it('shows in-scope and excluded counts in summary bar', async () => {
    vi.spyOn(erpApi, 'listErpRecords').mockResolvedValueOnce([RACK, EXCLUDED_PSU])
    const w = mount(ErpDatabaseView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('1 in scope')
    expect(w.text()).toContain('1 excluded')
  })

  it('renders in-scope badge', async () => {
    vi.spyOn(erpApi, 'listErpRecords').mockResolvedValueOnce([RACK])
    const w = mount(ErpDatabaseView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.find('.badge-inscope').text()).toBe('In Scope')
  })

  it('renders excluded badge', async () => {
    vi.spyOn(erpApi, 'listErpRecords').mockResolvedValueOnce([EXCLUDED_PSU])
    const w = mount(ErpDatabaseView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.find('.badge-excluded').text()).toBe('Excluded')
  })

  // BOM tree mode ──────────────────────────────────────────────────────────────

  it('shows BOM tree mode tag when scope is "all" and no search', async () => {
    vi.spyOn(erpApi, 'listErpRecords').mockResolvedValueOnce([RACK, BLADE])
    const w = mount(ErpDatabaseView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.find('.mode-tag').text()).toBe('BOM tree')
  })

  it('root CI with children shows expand button', async () => {
    vi.spyOn(erpApi, 'listErpRecords').mockResolvedValueOnce([RACK, BLADE])
    const w = mount(ErpDatabaseView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.find('.expand-btn').exists()).toBe(true)
  })

  it('child CI shows leaf indicator', async () => {
    vi.spyOn(erpApi, 'listErpRecords').mockResolvedValueOnce([RACK, BLADE])
    const w = mount(ErpDatabaseView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.find('.tree-leaf').exists()).toBe(true)
  })

  it('clicking expand button collapses children', async () => {
    vi.spyOn(erpApi, 'listErpRecords').mockResolvedValueOnce([RACK, BLADE])
    const w = mount(ErpDatabaseView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    const ciRows = w.find('table tbody')
    expect(ciRows.findAll('tr').length).toBe(2) // rack + blade
    await w.find('.expand-btn').trigger('click')
    expect(w.find('table tbody').findAll('tr').length).toBe(1) // blade hidden
  })

  // Flat mode & search ─────────────────────────────────────────────────────────

  it('shows flat list mode tag when scope filter is not "all"', async () => {
    vi.spyOn(erpApi, 'listErpRecords').mockResolvedValueOnce([RACK, EXCLUDED_PSU])
    const w = mount(ErpDatabaseView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    await w.findAll('.filter-btn')[1].trigger('click') // "In Scope"
    expect(w.find('.mode-tag').text()).toBe('flat list')
  })

  it('scope filter "In Scope" shows only in-scope records', async () => {
    vi.spyOn(erpApi, 'listErpRecords').mockResolvedValueOnce([RACK, EXCLUDED_PSU])
    const w = mount(ErpDatabaseView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    await w.findAll('.filter-btn')[1].trigger('click')
    expect(w.find('table tbody').findAll('tr').length).toBe(1)
    expect(w.find('table tbody').text()).toContain('SN-RACK-0001')
  })

  it('scope filter "Excluded" shows only out-of-scope records', async () => {
    vi.spyOn(erpApi, 'listErpRecords').mockResolvedValueOnce([RACK, EXCLUDED_PSU])
    const w = mount(ErpDatabaseView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    await w.findAll('.filter-btn')[2].trigger('click')
    expect(w.find('table tbody').findAll('tr').length).toBe(1)
    expect(w.find('table tbody').text()).toContain('SN-PSU-0002')
  })

  it('search box filters records by serial', async () => {
    vi.spyOn(erpApi, 'listErpRecords').mockResolvedValueOnce([RACK, EXCLUDED_PSU])
    const w = mount(ErpDatabaseView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    await w.find('.search-box').setValue('PSU')
    await flushPromises()
    expect(w.find('table tbody').findAll('tr').length).toBe(1)
    expect(w.find('table tbody').text()).toContain('SN-PSU-0002')
  })

  it('search box switches to flat list mode tag', async () => {
    vi.spyOn(erpApi, 'listErpRecords').mockResolvedValueOnce([RACK, BLADE])
    const w = mount(ErpDatabaseView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    await w.find('.search-box').setValue('rack')
    await flushPromises()
    expect(w.find('.mode-tag').text()).toBe('flat list')
  })

  it('shows "No records match" when search yields no results', async () => {
    vi.spyOn(erpApi, 'listErpRecords').mockResolvedValueOnce([RACK])
    const w = mount(ErpDatabaseView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    await w.find('.search-box').setValue('zzz-nomatch')
    await flushPromises()
    expect(w.text()).toContain('No records match')
  })

  // Expandable detail panel ────────────────────────────────────────────────────

  it('clicking a row shows the detail panel', async () => {
    vi.spyOn(erpApi, 'listErpRecords').mockResolvedValueOnce([RACK])
    const w = mount(ErpDatabaseView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.find('.detail-panel').exists()).toBe(false)
    await w.find('.data-row').trigger('click')
    expect(w.find('.detail-panel').exists()).toBe(true)
  })

  it('clicking the same row again hides the detail panel', async () => {
    vi.spyOn(erpApi, 'listErpRecords').mockResolvedValueOnce([RACK])
    const w = mount(ErpDatabaseView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    await w.find('.data-row').trigger('click')
    await w.find('.data-row').trigger('click')
    expect(w.find('.detail-panel').exists()).toBe(false)
  })

  it('detail panel shows technician name with GDPR tag', async () => {
    vi.spyOn(erpApi, 'listErpRecords').mockResolvedValueOnce([RACK])
    const w = mount(ErpDatabaseView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    await w.find('.data-row').trigger('click')
    const panel = w.find('.detail-panel')
    expect(panel.text()).toContain('Klaus Bauer')
    expect(panel.find('.tag-gdpr').exists()).toBe(true)
  })

  it('detail panel shows storage location with Open Point tag', async () => {
    vi.spyOn(erpApi, 'listErpRecords').mockResolvedValueOnce([RACK])
    const w = mount(ErpDatabaseView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    await w.find('.data-row').trigger('click')
    const panel = w.find('.detail-panel')
    expect(panel.text()).toContain('Halle A, Reihe 3')
    expect(panel.find('.tag-pending').exists()).toBe(true)
  })

  // Excluded fields note at bottom ─────────────────────────────────────────────

  it('shows excluded fields note with GDPR and Open Point rows', async () => {
    vi.spyOn(erpApi, 'listErpRecords').mockResolvedValueOnce([RACK])
    const w = mount(ErpDatabaseView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('technician_name')
    expect(w.text()).toContain('GDPR')
    expect(w.text()).toContain('storage_location')
    expect(w.text()).toContain('Open Point #4')
  })
})
