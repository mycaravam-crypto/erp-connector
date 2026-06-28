import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import ExportList from '@/views/ExportList.vue'
import * as exportsApi from '@/api/exports'

function buildRouter() {
  const r = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', name: 'exports', component: ExportList },
      { path: '/exports/:seqNo', name: 'export-detail', component: { template: '<div/>' } },
    ],
  })
  r.push('/')
  return r
}

beforeEach(() => vi.restoreAllMocks())

const RUNS = [
  { sequenceNo: 2, extractedAt: '2026-06-28T10:44:28Z', recordCount: 5, sha256Short: 'fc5b80749393', status: 'Pending', dataFileName: 'export_0002.xlsx' },
  { sequenceNo: 1, extractedAt: '2026-06-28T10:42:27Z', recordCount: 0, sha256Short: '', status: 'Failed', dataFileName: '' },
]

describe('ExportList', () => {
  it('shows loading initially', () => {
    vi.spyOn(exportsApi, 'listExports').mockReturnValue(new Promise(() => {}))
    const w = mount(ExportList, { global: { plugins: [buildRouter()] } })
    expect(w.text()).toContain('Loading')
  })

  it('renders a row per run', async () => {
    vi.spyOn(exportsApi, 'listExports').mockResolvedValueOnce(RUNS)
    const w = mount(ExportList, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.findAll('tbody tr')).toHaveLength(2)
  })

  it('shows sequence number as a link', async () => {
    vi.spyOn(exportsApi, 'listExports').mockResolvedValueOnce(RUNS)
    const w = mount(ExportList, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.find('tbody tr td a').text()).toBe('2')
  })

  it('applies correct badge class for Pending and Failed', async () => {
    vi.spyOn(exportsApi, 'listExports').mockResolvedValueOnce(RUNS)
    const w = mount(ExportList, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    const badges = w.findAll('.badge')
    expect(badges[0].classes()).toContain('badge-pending')
    expect(badges[1].classes()).toContain('badge-failed')
  })

  it('shows empty state when no runs', async () => {
    vi.spyOn(exportsApi, 'listExports').mockResolvedValueOnce([])
    const w = mount(ExportList, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('No export runs')
  })

  it('shows error message when fetch throws', async () => {
    vi.spyOn(exportsApi, 'listExports').mockRejectedValueOnce(new Error('network'))
    const w = mount(ExportList, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('Could not reach')
  })
})
