import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import ExportDetail from '@/views/ExportDetail.vue'
import * as exportsApi from '@/api/exports'

const PENDING = {
  id: 4, sequenceNo: 4, extractedAt: '2026-06-28T10:44:28Z', recordCount: 5,
  sha256: 'fc5b80749393abc123', status: 'Pending', releasedAt: null,
  operatedBy: null, approvedBy: null, dataFileName: 'export_0004.xlsx',
}

const RELEASED = { ...PENDING, status: 'Released', releasedAt: '2026-06-28T11:00:00Z', operatedBy: 'alice', approvedBy: 'bob' }

function buildRouter(seqNo = 4) {
  const r = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { template: '<div/>' } },
      { path: '/exports/:seqNo', component: ExportDetail },
    ],
  })
  r.push(`/exports/${seqNo}`)
  return r
}

beforeEach(() => vi.restoreAllMocks())

describe('ExportDetail', () => {
  it('shows loading initially', () => {
    vi.spyOn(exportsApi, 'getExport').mockReturnValue(new Promise(() => {}))
    const w = mount(ExportDetail, { global: { plugins: [buildRouter()] } })
    expect(w.text()).toContain('Loading')
  })

  it('renders full SHA and record count', async () => {
    vi.spyOn(exportsApi, 'getExport').mockResolvedValueOnce(PENDING)
    const w = mount(ExportDetail, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('fc5b80749393abc123')
    expect(w.text()).toContain('5')
  })

  it('shows ReleaseDialog only for Pending runs', async () => {
    vi.spyOn(exportsApi, 'getExport').mockResolvedValueOnce(PENDING)
    const w = mount(ExportDetail, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.find('.release-dialog').exists()).toBe(true)
  })

  it('hides ReleaseDialog for Released runs', async () => {
    vi.spyOn(exportsApi, 'getExport').mockResolvedValueOnce(RELEASED)
    const w = mount(ExportDetail, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.find('.release-dialog').exists()).toBe(false)
  })

  it('shows releasedAt, operatedBy, approvedBy when Released', async () => {
    vi.spyOn(exportsApi, 'getExport').mockResolvedValueOnce(RELEASED)
    const w = mount(ExportDetail, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('alice')
    expect(w.text()).toContain('bob')
  })

  it('shows not-found message on 404', async () => {
    vi.spyOn(exportsApi, 'getExport').mockResolvedValueOnce(null)
    const w = mount(ExportDetail, { global: { plugins: [buildRouter(999)] } })
    await flushPromises()
    expect(w.text()).toContain('not found')
  })

  it('reloads when released event fires', async () => {
    const spy = vi.spyOn(exportsApi, 'getExport')
      .mockResolvedValueOnce(PENDING)
      .mockResolvedValueOnce(RELEASED)
    const w = mount(ExportDetail, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    w.findComponent({ name: 'ReleaseDialog' }).vm.$emit('released')
    await flushPromises()
    expect(spy).toHaveBeenCalledTimes(2)
    expect(w.find('.release-dialog').exists()).toBe(false)
  })
})
