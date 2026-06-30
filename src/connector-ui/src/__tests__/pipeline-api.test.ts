import { describe, it, expect, vi, beforeEach } from 'vitest'
import { runNow, getPreview } from '@/api/pipeline'

function mockFetch(body: unknown, status = 200) {
  return vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
    ok: status >= 200 && status < 300,
    status,
    json: async () => body,
    text: async () => (typeof body === 'string' ? body : JSON.stringify(body)),
  } as Response)
}

beforeEach(() => vi.restoreAllMocks())

describe('runNow', () => {
  it('returns ok:true with data on success (default xlsx)', async () => {
    mockFetch({ sequenceNo: 3, recordCount: 5, sha256Short: 'abc123def456' })
    const result = await runNow()
    expect(result.ok).toBe(true)
    expect(result.data?.sequenceNo).toBe(3)
    expect(result.data?.recordCount).toBe(5)
    expect(fetch).toHaveBeenCalledWith(
      '/api/pipeline/run?format=xlsx',
      expect.objectContaining({ method: 'POST' }),
    )
  })

  it('passes the requested format in the URL', async () => {
    mockFetch({ sequenceNo: 4, recordCount: 5, sha256Short: 'abc123def456' })
    await runNow('csv')
    expect(fetch).toHaveBeenCalledWith(
      '/api/pipeline/run?format=csv',
      expect.objectContaining({ method: 'POST' }),
    )
  })

  it('returns ok:false with error detail from JSON problem response on 500', async () => {
    // Server returns RFC 7807 problem JSON: { detail: "..." }
    mockFetch({ detail: 'Staging-Pfad existiert nicht' }, 500)
    const result = await runNow()
    expect(result.ok).toBe(false)
    expect(result.error).toContain('Staging-Pfad')
  })

  it('returns ok:false with fallback message when JSON has no detail or title', async () => {
    mockFetch({}, 500)
    const result = await runNow()
    expect(result.ok).toBe(false)
    expect(result.error).toContain('500')
  })
})

describe('getPreview', () => {
  const PREVIEW = {
    recordCount: 2,
    schemaVersion: '2.0',
    columns: ['guid', 'serial_number', 'part_number', 'parent_serial_number', 'model_reference', 'commissioning_date', 'maintenance_state'],
    records: [
      { guid: 'sc-rack-0001', serial_number: 'SN-RACK-0001', part_number: 'P-RACK-42U', parent_serial_number: '', model_reference: 'Industrial Rack System', commissioning_date: '2023-03-01', maintenance_state: 'Active' },
      { guid: 'sc-blade-0001', serial_number: 'SN-BLD-0001', part_number: 'P-BLADE-CM2', parent_serial_number: 'SN-RACK-0001', model_reference: 'Compute Module MK2', commissioning_date: '2023-03-15', maintenance_state: 'Active' },
    ],
  }

  it('fetches /api/pipeline/preview and returns the result', async () => {
    mockFetch(PREVIEW)
    const result = await getPreview()
    expect(result?.recordCount).toBe(2)
    expect(result?.schemaVersion).toBe('2.0')
    expect(result?.columns).toHaveLength(7)
    expect(result?.records).toHaveLength(2)
    expect(fetch).toHaveBeenCalledWith('/api/pipeline/preview', expect.any(Object))
  })

  it('returns null on non-ok response', async () => {
    mockFetch(null, 401)
    const result = await getPreview()
    expect(result).toBeNull()
  })
})
