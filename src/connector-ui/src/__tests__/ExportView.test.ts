import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import ExportView from '@/views/ExportView.vue'
import * as pipelineApi from '@/api/pipeline'
import * as erpApi from '@/api/erp'
import * as exportsApi from '@/api/exports'
import type { ExportMappingConfig } from '@/api/erp'
import type { PreviewResult } from '@/api/pipeline'
import type { ExportSummary } from '@/api/exports'

function buildRouter() {
  const r = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/exports', name: 'exports', component: ExportView },
      { path: '/exports/:seqNo', name: 'export-detail', component: { template: '<div/>' } },
      { path: '/export-schema', name: 'export-schema', component: { template: '<div/>' } },
    ],
  })
  r.push('/exports')
  return r
}

const MAPPING: ExportMappingConfig = {
  sourceTable: 'systemconfiguration',
  fields: [
    { sourceName: 'id',     targetName: 'guid',          enabled: true },
    { sourceName: 'serial', targetName: 'serial_number', enabled: true },
    { sourceName: 'status', targetName: 'status',        enabled: false },
  ],
  relations: [],
}

const PREVIEW: PreviewResult = {
  recordCount: 2,
  schemaVersion: '2.0',
  columns: ['guid', 'serial_number'],
  records: [
    { guid: 'a1', serial_number: 'SN-001' },
    { guid: 'a2', serial_number: 'SN-002' },
  ],
  source: 'dynamic',
}

const RUN: ExportSummary = {
  sequenceNo: 3,
  extractedAt: '2026-06-28T10:44:28',
  recordCount: 5,
  sha256Short: 'abc123def',
  status: 'Pending',
  dataFileName: 'export_0003.xlsx',
  isStale: false,
}

beforeEach(() => {
  vi.restoreAllMocks()
  vi.spyOn(pipelineApi, 'getPreview').mockResolvedValue(PREVIEW)
  vi.spyOn(exportsApi, 'listExports').mockResolvedValue([])
  vi.spyOn(erpApi, 'getExportMapping').mockResolvedValue(null)
})

describe('ExportView', () => {
  it('shows preview loading state initially', () => {
    vi.spyOn(pipelineApi, 'getPreview').mockReturnValue(new Promise(() => {}))
    const w = mount(ExportView, { global: { plugins: [buildRouter()] } })
    expect(w.text()).toContain('Loading preview')
  })

  it('renders preview table with columns and rows', async () => {
    const w = mount(ExportView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('guid')
    expect(w.text()).toContain('SN-001')
  })

  it('shows "Showing N of M" note when preview exceeds 20 rows', async () => {
    const manyRecords = Array.from({ length: 25 }, (_, i) => ({
      guid: `g${i}`, serial_number: `SN-${i}`,
    }))
    vi.spyOn(pipelineApi, 'getPreview').mockResolvedValueOnce({
      ...PREVIEW,
      recordCount: 25,
      records: manyRecords,
    })
    const w = mount(ExportView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('Showing first 20 rows')
  })

  it('does not show "Showing N of M" when rows ≤ 20', async () => {
    const w = mount(ExportView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).not.toContain('Showing')
  })

  it('does not show "No in-scope records found" when the preview table has rows', async () => {
    const w = mount(ExportView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('SN-001')
    expect(w.text()).not.toContain('No in-scope records found')
  })

  it('does not show "No in-scope records found" alongside the preview-failed error box', async () => {
    vi.spyOn(pipelineApi, 'getPreview').mockResolvedValueOnce({
      recordCount: 0, schemaVersion: '2.0', columns: [], records: [], source: 'error',
      error: 'No export mapping configured.',
    })
    const w = mount(ExportView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('No export mapping configured.')
    expect(w.text()).not.toContain('No in-scope records found')
  })

  it('shows active mapping summary with source table and field count', async () => {
    vi.spyOn(erpApi, 'getExportMapping').mockResolvedValueOnce(MAPPING)
    const w = mount(ExportView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('systemconfiguration')
    expect(w.text()).toContain('2 fields')
  })

  it('shows one chip per enabled relation field, not per relation', async () => {
    vi.spyOn(erpApi, 'getExportMapping').mockResolvedValueOnce({
      ...MAPPING,
      relations: [
        {
          relatedTable: 'masterdata',
          joinKey: 'id',
          sourceJoinKey: 'article_id',
          enabled: true,
          flattenStrategy: 'string_join',
          delimiter: ', ',
          fields: [
            { sourceField: 'article_name', targetField: 'article_names', enabled: true },
            { sourceField: 'part_number', targetField: 'part_numbers', enabled: true },
            { sourceField: 'manufacturer', targetField: 'manufacturer', enabled: false },
          ],
        },
      ],
    })
    const w = mount(ExportView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    expect(w.text()).toContain('masterdata.article_name')
    expect(w.text()).toContain('article_names')
    expect(w.text()).toContain('masterdata.part_number')
    expect(w.text()).toContain('part_numbers')
    // Disabled field must not produce a chip.
    expect(w.text()).not.toContain('masterdata.manufacturer')
  })

  it('shows "Not configured" badge when no mapping exists', async () => {
    const w = mount(ExportView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('Not configured')
  })

  it('shows export runs in the history table', async () => {
    vi.spyOn(exportsApi, 'listExports').mockResolvedValueOnce([RUN])
    const w = mount(ExportView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('abc123def')
    expect(w.text()).toContain('export_0003.xlsx')
  })

  it('shows stale alert when any run isStale', async () => {
    vi.spyOn(exportsApi, 'listExports').mockResolvedValueOnce([{ ...RUN, isStale: true }])
    const w = mount(ExportView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('Action required')
  })

  it('shows "No export runs yet" when the list is empty', async () => {
    const w = mount(ExportView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('No export runs yet')
  })

  it('shows three format toggle buttons (Excel, CSV, JSON)', async () => {
    const w = mount(ExportView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('Excel')
    expect(w.text()).toContain('CSV')
    expect(w.text()).toContain('JSON')
  })

  it('calls runNow and shows success banner with sequence number and record count', async () => {
    vi.spyOn(pipelineApi, 'runNow').mockResolvedValueOnce({
      ok: true,
      data: { sequenceNo: 7, recordCount: 10, sha256Short: 'deadbeef12' },
    })
    const w = mount(ExportView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    const runBtn = w.findAll('button').find((b) => b.text().includes('Export as'))!
    await runBtn.trigger('click')
    await flushPromises()

    expect(pipelineApi.runNow).toHaveBeenCalled()
    expect(w.text()).toContain('#7')
    expect(w.text()).toContain('10 records')
  })

  it('shows error banner when runNow fails', async () => {
    vi.spyOn(pipelineApi, 'runNow').mockResolvedValueOnce({
      ok: false, error: 'No mapping configured.',
    })
    const w = mount(ExportView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    const runBtn = w.findAll('button').find((b) => b.text().includes('Export as'))!
    await runBtn.trigger('click')
    await flushPromises()

    expect(w.text()).toContain('No mapping configured.')
  })

  it('does not render the raw ISO extractedAt string in the runs table', async () => {
    vi.spyOn(exportsApi, 'listExports').mockResolvedValueOnce([RUN])
    const w = mount(ExportView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).not.toContain('2026-06-28T10:44:28')
  })

  it('renders SHA as a clickable copy button in the runs table', async () => {
    vi.spyOn(exportsApi, 'listExports').mockResolvedValueOnce([RUN])
    const w = mount(ExportView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    const copyBtn = w.findAll('button').find((b) => b.text().includes('abc123def'))
    expect(copyBtn).toBeTruthy()
  })

  it('shows preview error state when source is "error"', async () => {
    vi.spyOn(pipelineApi, 'getPreview').mockResolvedValueOnce({
      recordCount: 0,
      schemaVersion: '2.0',
      columns: [],
      records: [],
      source: 'error',
      error: 'No export mapping configured.',
    })
    const w = mount(ExportView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('No export mapping configured.')
  })

  it('shows "No in-scope records found" when preview returns empty records', async () => {
    vi.spyOn(pipelineApi, 'getPreview').mockResolvedValueOnce({
      ...PREVIEW, recordCount: 0, records: [], source: 'dynamic',
    })
    const w = mount(ExportView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('No in-scope records found')
  })
})
