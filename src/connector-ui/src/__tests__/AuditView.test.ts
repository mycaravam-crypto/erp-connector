import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import AuditView from '@/views/AuditView.vue'
import * as auditApi from '@/api/audit'

const ENTRIES: auditApi.AuditEntry[] = [
  { id: 1, timestamp: '2026-01-05T08:00:00Z', username: 'alice', action: 'export.run', detail: null },
  { id: 2, timestamp: '2026-01-05T09:30:00+00:00', username: 'bob', action: 'settings.save', detail: 'retentionDays=30' },
]

beforeEach(() => vi.restoreAllMocks())

describe('AuditView', () => {
  it('shows loading state initially', () => {
    vi.spyOn(auditApi, 'getAuditLog').mockReturnValue(new Promise(() => {}))
    const w = mount(AuditView)
    expect(w.text()).toContain('Loading')
  })

  it('shows error when the API throws', async () => {
    vi.spyOn(auditApi, 'getAuditLog').mockRejectedValueOnce(new Error('network'))
    const w = mount(AuditView)
    await flushPromises()
    expect(w.text()).toContain('Could not load audit log')
  })

  it('shows an empty state when there are no entries', async () => {
    vi.spyOn(auditApi, 'getAuditLog').mockResolvedValueOnce([])
    const w = mount(AuditView)
    await flushPromises()
    expect(w.text()).toContain('No audit entries yet.')
  })

  it('renders entry rows with username, action, and detail', async () => {
    vi.spyOn(auditApi, 'getAuditLog').mockResolvedValueOnce(ENTRIES)
    const w = mount(AuditView)
    await flushPromises()
    expect(w.text()).toContain('alice')
    expect(w.text()).toContain('export.run')
    expect(w.text()).toContain('bob')
    expect(w.text()).toContain('retentionDays=30')
  })

  it('shows an em dash for a null detail', async () => {
    vi.spyOn(auditApi, 'getAuditLog').mockResolvedValueOnce(ENTRIES)
    const w = mount(AuditView)
    await flushPromises()
    expect(w.text()).toContain('—')
  })

  it('reloads the log when Refresh is clicked', async () => {
    const spy = vi.spyOn(auditApi, 'getAuditLog').mockResolvedValue(ENTRIES)
    const w = mount(AuditView)
    await flushPromises()
    await w.find('button').trigger('click')
    await flushPromises()
    expect(spy).toHaveBeenCalledTimes(2)
  })
})
