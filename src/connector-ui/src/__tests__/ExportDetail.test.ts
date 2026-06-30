import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import ExportDetail from '@/views/ExportDetail.vue'
import * as exportsApi from '@/api/exports'

// Base fixture — all new nullable fields set to null
const PENDING = {
  id: 4, sequenceNo: 4, extractedAt: '2026-06-28T10:44:28Z', recordCount: 5,
  sha256: 'fc5b80749393abc123', status: 'Pending', releasedAt: null,
  operatedBy: null, approvedBy: null, dataFileName: 'export_0004.xlsx',
  deliveredAt: null, deliveredBy: null, importedRecordCount: null, deliveryNotes: null,
  sequenceGapWarning: null,
}

const RELEASED = {
  ...PENDING, status: 'Released', releasedAt: '2026-06-28T11:00:00Z',
  operatedBy: 'alice', approvedBy: 'bob',
}

const DELIVERED = {
  ...RELEASED, deliveredAt: '2026-06-28T14:00:00Z', deliveredBy: 'alice',
  importedRecordCount: 5, deliveryNotes: 'USB-007',
}

const GAP_WARNING = {
  ...PENDING,
  sequenceGapWarning: 'Sequence gap detected: last released run is #2, but #4 is next in line.',
}

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

  // ── Sequence gap warning (Phase 6.3) ──────────────────────────────────────

  it('shows gap warning banner when sequenceGapWarning is set', async () => {
    vi.spyOn(exportsApi, 'getExport').mockResolvedValueOnce(GAP_WARNING)
    const w = mount(ExportDetail, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.find('.gap-warning').exists()).toBe(true)
    expect(w.text()).toContain('Sequence gap detected')
  })

  it('hides gap warning when sequenceGapWarning is null', async () => {
    vi.spyOn(exportsApi, 'getExport').mockResolvedValueOnce(PENDING)
    const w = mount(ExportDetail, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.find('.gap-warning').exists()).toBe(false)
  })

  // ── Delivery acknowledgement (Phase 6.4) ──────────────────────────────────

  it('shows delivery form for Released runs not yet delivered', async () => {
    vi.spyOn(exportsApi, 'getExport').mockResolvedValueOnce(RELEASED)
    const w = mount(ExportDetail, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.find('.delivery-card').exists()).toBe(true)
  })

  it('hides delivery form when already delivered', async () => {
    vi.spyOn(exportsApi, 'getExport').mockResolvedValueOnce(DELIVERED)
    const w = mount(ExportDetail, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.find('.delivery-card').exists()).toBe(false)
  })

  it('shows delivery-done indicator when deliveredAt is set', async () => {
    vi.spyOn(exportsApi, 'getExport').mockResolvedValueOnce(DELIVERED)
    const w = mount(ExportDetail, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.find('.delivery-done').exists()).toBe(true)
    expect(w.text()).toContain('USB-007')
  })

  it('does not render raw ISO date string for extractedAt', async () => {
    vi.spyOn(exportsApi, 'getExport').mockResolvedValueOnce(PENDING)
    const w = mount(ExportDetail, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).not.toContain('2026-06-28T10:44:28Z')
  })

  it('shows SHA copy button containing the full hash', async () => {
    vi.spyOn(exportsApi, 'getExport').mockResolvedValueOnce(PENDING)
    const w = mount(ExportDetail, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    const copyBtn = w.findAll('button').find((b) => b.text().includes('fc5b80749393abc123'))
    expect(copyBtn).toBeTruthy()
  })

  it('shows error message when deliverExport fails', async () => {
    vi.spyOn(exportsApi, 'getExport').mockResolvedValueOnce(RELEASED)
    vi.spyOn(exportsApi, 'deliverExport').mockResolvedValueOnce({
      ok: false, status: 409, message: 'Already delivered.',
    })
    const w = mount(ExportDetail, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    await w.find('.delivery-card button').trigger('click')
    await flushPromises()
    expect(w.text()).toContain('Already delivered.')
  })

  it('calls deliverExport and reloads on submit', async () => {
    vi.spyOn(exportsApi, 'getExport')
      .mockResolvedValueOnce(RELEASED)
      .mockResolvedValueOnce(DELIVERED)
    vi.spyOn(exportsApi, 'deliverExport').mockResolvedValueOnce({ ok: true, status: 200, message: '' })

    const w = mount(ExportDetail, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    await w.find('#import-count').setValue('5')
    await w.find('#delivery-notes').setValue('USB-007')
    await w.find('.delivery-card button').trigger('click')
    await flushPromises()
    expect(exportsApi.deliverExport).toHaveBeenCalledWith(4, { importedRecordCount: 5, notes: 'USB-007' })
    expect(w.find('.delivery-done').exists()).toBe(true)
  })
})
