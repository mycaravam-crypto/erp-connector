import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import PipelineView from '@/views/PipelineView.vue'
import * as pipelineApi from '@/api/pipeline'
import type { PreviewResult } from '@/api/pipeline'

function buildRouter() {
  const r = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/pipeline', name: 'pipeline', component: PipelineView },
      { path: '/exports/:seqNo', name: 'export-detail', component: { template: '<div/>' } },
    ],
  })
  r.push('/pipeline')
  return r
}

beforeEach(() => vi.restoreAllMocks())

const PREVIEW: PreviewResult = {
  recordCount: 2,
  schemaVersion: '2.0',
  records: [
    {
      guid: 'sc-rack-0001', serialNumber: 'SN-RACK-0001', partNumber: 'P-RACK-42U',
      parentSerialNumber: null, modelReference: 'Industrial Rack System',
      commissioningDate: '2023-03-01', maintenanceState: 'Active',
    },
    {
      guid: 'sc-blade-0001', serialNumber: 'SN-BLD-0001', partNumber: 'P-BLADE-CM2',
      parentSerialNumber: 'SN-RACK-0001', modelReference: 'Compute Module MK2',
      commissioningDate: '2023-03-15', maintenanceState: 'Active',
    },
  ],
}

describe('PipelineView', () => {
  // Preview loading ─────────────────────────────────────────────────────────────

  it('shows loading preview initially', () => {
    vi.spyOn(pipelineApi, 'getPreview').mockReturnValue(new Promise(() => {}))
    const w = mount(PipelineView, { global: { plugins: [buildRouter()] } })
    expect(w.text()).toContain('Loading preview')
  })

  it('renders preview table with one row per record', async () => {
    vi.spyOn(pipelineApi, 'getPreview').mockResolvedValueOnce(PREVIEW)
    const w = mount(PipelineView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.findAll('tbody tr')).toHaveLength(2)
  })

  it('shows record count and schema version in preview header', async () => {
    vi.spyOn(pipelineApi, 'getPreview').mockResolvedValueOnce(PREVIEW)
    const w = mount(PipelineView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.find('.preview-meta').text()).toContain('2 records')
    expect(w.find('.preview-meta').text()).toContain('v2.0')
  })

  it('shows serial number and guid in the row', async () => {
    vi.spyOn(pipelineApi, 'getPreview').mockResolvedValueOnce(PREVIEW)
    const w = mount(PipelineView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    const firstRow = w.find('tbody tr')
    expect(firstRow.text()).toContain('sc-rack-0001')
    expect(firstRow.text()).toContain('SN-RACK-0001')
  })

  it('shows maintenance_state badge with correct class for Active', async () => {
    vi.spyOn(pipelineApi, 'getPreview').mockResolvedValueOnce(PREVIEW)
    const w = mount(PipelineView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.find('.state-active').exists()).toBe(true)
  })

  it('shows error when getPreview fails', async () => {
    vi.spyOn(pipelineApi, 'getPreview').mockRejectedValueOnce(new Error('network'))
    const w = mount(PipelineView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('Could not reach')
  })

  it('shows empty state message when no records', async () => {
    vi.spyOn(pipelineApi, 'getPreview').mockResolvedValueOnce({
      recordCount: 0, schemaVersion: '2.0', records: [],
    })
    const w = mount(PipelineView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('No in-scope records')
  })

  // Run Now ─────────────────────────────────────────────────────────────────────

  it('shows Run Now button', async () => {
    vi.spyOn(pipelineApi, 'getPreview').mockResolvedValueOnce(PREVIEW)
    const w = mount(PipelineView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.find('.run-btn').text()).toBe('Run Now')
  })

  it('shows success banner after successful run', async () => {
    vi.spyOn(pipelineApi, 'getPreview').mockResolvedValue(PREVIEW)
    vi.spyOn(pipelineApi, 'runNow').mockResolvedValueOnce({
      ok: true,
      data: { sequenceNo: 7, recordCount: 2, sha256Short: 'abc123def456' },
    })
    const w = mount(PipelineView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    await w.find('.run-btn').trigger('click')
    await flushPromises()
    const banner = w.find('.result-ok')
    expect(banner.exists()).toBe(true)
    expect(banner.text()).toContain('#7')
    expect(banner.text()).toContain('2 records')
    expect(banner.text()).toContain('abc123def456')
  })

  it('shows error banner when run fails', async () => {
    vi.spyOn(pipelineApi, 'getPreview').mockResolvedValueOnce(PREVIEW)
    vi.spyOn(pipelineApi, 'runNow').mockResolvedValueOnce({
      ok: false,
      error: 'Staging-Pfad existiert nicht',
    })
    const w = mount(PipelineView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    await w.find('.run-btn').trigger('click')
    await flushPromises()
    const banner = w.find('.result-err')
    expect(banner.exists()).toBe(true)
    expect(banner.text()).toContain('Staging-Pfad')
  })

  it('refreshes preview after successful run', async () => {
    const getPreviewSpy = vi.spyOn(pipelineApi, 'getPreview').mockResolvedValue(PREVIEW)
    vi.spyOn(pipelineApi, 'runNow').mockResolvedValueOnce({
      ok: true,
      data: { sequenceNo: 7, recordCount: 2, sha256Short: 'abc123def456' },
    })
    const w = mount(PipelineView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    const callsBefore = getPreviewSpy.mock.calls.length
    await w.find('.run-btn').trigger('click')
    await flushPromises()
    expect(getPreviewSpy.mock.calls.length).toBeGreaterThan(callsBefore)
  })

  it('success banner has a "View → Release" link button', async () => {
    vi.spyOn(pipelineApi, 'getPreview').mockResolvedValue(PREVIEW)
    vi.spyOn(pipelineApi, 'runNow').mockResolvedValueOnce({
      ok: true,
      data: { sequenceNo: 7, recordCount: 2, sha256Short: 'abc123def456' },
    })
    const w = mount(PipelineView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    await w.find('.run-btn').trigger('click')
    await flushPromises()
    expect(w.find('.result-link').text()).toContain('View')
  })
})
