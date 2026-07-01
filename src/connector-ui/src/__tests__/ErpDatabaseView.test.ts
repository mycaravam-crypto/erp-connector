import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import ErpDatabaseView from '@/views/ErpDatabaseView.vue'
import * as erpApi from '@/api/erp'
import type { ErpCiRecord } from '@/api/erp'

function buildRouter() {
  const r = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/erp-database', name: 'erp-database', component: ErpDatabaseView }],
  })
  r.push('/erp-database')
  return r
}

function makeRecord(overrides: Partial<ErpCiRecord> = {}): ErpCiRecord {
  return {
    id: 'id-1',
    serial: 'SN-001',
    status: 'Active',
    commissionDate: '2023-01-15',
    articleName: 'Main Unit',
    partNumber: 'P-001',
    manufacturer: 'TechCorp',
    maintenancePlanStatus: 'Active',
    allocationChartRef: 'ALLOC-2023',
    parentId: null,
    parentSerial: null,
    inScope: true,
    exclusionReason: null,
    technicianName: 'Klaus Bauer',
    storageLocation: 'Halle A',
    ...overrides,
  }
}

const ROOT = makeRecord({ id: 'root-1', serial: 'SN-ROOT', inScope: true })
const CHILD = makeRecord({
  id: 'child-1',
  serial: 'SN-CHILD',
  parentId: 'root-1',
  parentSerial: 'SN-ROOT',
  inScope: false,
  exclusionReason: 'No maintenance plan',
})
const EXCLUDED = makeRecord({ id: 'excl-1', serial: 'SN-EXCL', inScope: false, exclusionReason: 'Inactive maintenance plan' })

beforeEach(() => vi.restoreAllMocks())

describe('ErpDatabaseView', () => {
  it('shows loading state initially', () => {
    vi.spyOn(erpApi, 'listErpRecords').mockReturnValue(new Promise(() => {}))
    const w = mount(ErpDatabaseView, { global: { plugins: [buildRouter()] } })
    expect(w.text()).toContain('Loading ERP records')
  })

  it('shows error when API throws', async () => {
    vi.spyOn(erpApi, 'listErpRecords').mockRejectedValueOnce(new Error('network'))
    const w = mount(ErpDatabaseView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('Could not reach')
  })

  it('shows total, in-scope, excluded counts', async () => {
    vi.spyOn(erpApi, 'listErpRecords').mockResolvedValueOnce([ROOT, CHILD, EXCLUDED])
    const w = mount(ErpDatabaseView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('3 CIs total')
    expect(w.text()).toContain('1 in scope')
    expect(w.text()).toContain('2 excluded')
  })

  it('renders BOM tree mode by default (root visible, child hidden)', async () => {
    vi.spyOn(erpApi, 'listErpRecords').mockResolvedValueOnce([ROOT, CHILD])
    const w = mount(ErpDatabaseView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('SN-ROOT')
    expect(w.text()).not.toContain('SN-CHILD')
  })

  it('expands children when + button is clicked', async () => {
    vi.spyOn(erpApi, 'listErpRecords').mockResolvedValueOnce([ROOT, CHILD])
    const w = mount(ErpDatabaseView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    const expandBtn = w.findAll('button').find((b) => b.text() === '+')!
    expect(expandBtn).toBeTruthy()
    await expandBtn.trigger('click')
    expect(w.text()).toContain('SN-CHILD')
  })

  it('collapses children when − button is clicked', async () => {
    vi.spyOn(erpApi, 'listErpRecords').mockResolvedValueOnce([ROOT, CHILD])
    const w = mount(ErpDatabaseView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    const expandBtn = w.findAll('button').find((b) => b.text() === '+')!
    await expandBtn.trigger('click')
    expect(w.text()).toContain('SN-CHILD')
    const collapseBtn = w.findAll('button').find((b) => b.text() === '−')!
    await collapseBtn.trigger('click')
    expect(w.text()).not.toContain('SN-CHILD')
  })

  it('shows detail panel with excluded fields on row click', async () => {
    vi.spyOn(erpApi, 'listErpRecords').mockResolvedValueOnce([ROOT])
    const w = mount(ErpDatabaseView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    const row = w.findAll('tr').find((r) => r.text().includes('SN-ROOT'))!
    await row.trigger('click')
    expect(w.text()).toContain('Klaus Bauer')
    expect(w.text()).toContain('GDPR')
    expect(w.text()).toContain('Halle A')
    expect(w.text()).toContain('Open Point #4')
  })

  it('hides detail panel on second click of same row', async () => {
    vi.spyOn(erpApi, 'listErpRecords').mockResolvedValueOnce([ROOT])
    const w = mount(ErpDatabaseView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    const row = w.findAll('tr').find((r) => r.text().includes('SN-ROOT'))!
    await row.trigger('click')
    expect(w.text()).toContain('Klaus Bauer')
    await row.trigger('click')
    expect(w.text()).not.toContain('Klaus Bauer')
  })

  it('switches to flat list mode when search is active', async () => {
    vi.spyOn(erpApi, 'listErpRecords').mockResolvedValueOnce([ROOT, CHILD])
    const w = mount(ErpDatabaseView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    await w.find('input[type="search"]').setValue('CHILD')
    expect(w.text()).toContain('Flat list mode')
    expect(w.text()).toContain('SN-CHILD')
  })

  it('filters by search across serial, model, part number', async () => {
    const r2 = makeRecord({ id: 'id-2', serial: 'SN-002', articleName: 'Sub Module', partNumber: 'P-002' })
    vi.spyOn(erpApi, 'listErpRecords').mockResolvedValueOnce([ROOT, r2])
    const w = mount(ErpDatabaseView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    await w.find('input[type="search"]').setValue('Sub Module')
    expect(w.text()).toContain('SN-002')
    expect(w.text()).not.toContain('SN-ROOT')
  })

  it('scope filter "In Scope" shows only in-scope records', async () => {
    vi.spyOn(erpApi, 'listErpRecords').mockResolvedValueOnce([ROOT, EXCLUDED])
    const w = mount(ErpDatabaseView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    const inScopeBtn = w.findAll('button').find((b) => b.text() === 'In Scope')!
    await inScopeBtn.trigger('click')
    expect(w.text()).toContain('SN-ROOT')
    expect(w.text()).not.toContain('SN-EXCL')
  })

  it('scope filter "Excluded" shows only excluded records', async () => {
    vi.spyOn(erpApi, 'listErpRecords').mockResolvedValueOnce([ROOT, EXCLUDED])
    const w = mount(ErpDatabaseView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    const excludedBtn = w.findAll('button').find((b) => b.text() === 'Excluded')!
    await excludedBtn.trigger('click')
    expect(w.text()).toContain('SN-EXCL')
    expect(w.text()).not.toContain('SN-ROOT')
  })

  it('shows exclusion reason for out-of-scope records', async () => {
    vi.spyOn(erpApi, 'listErpRecords').mockResolvedValueOnce([EXCLUDED])
    const w = mount(ErpDatabaseView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('Inactive maintenance plan')
  })

  it('shows "no records match" message when filter yields nothing', async () => {
    vi.spyOn(erpApi, 'listErpRecords').mockResolvedValueOnce([ROOT])
    const w = mount(ErpDatabaseView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    await w.find('input[type="search"]').setValue('zzz-no-match')
    expect(w.text()).toContain('No records match')
  })
})
