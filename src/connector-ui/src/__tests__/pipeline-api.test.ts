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

  it('returns ok:false with error text on 500', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: false,
      status: 500,
      text: async () => 'Staging-Pfad existiert nicht',
    } as Response)
    const result = await runNow()
    expect(result.ok).toBe(false)
    expect(result.error).toContain('Staging-Pfad')
  })

  it('returns ok:false with fallback message on empty error body', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: false,
      status: 500,
      text: async () => '',
    } as Response)
    const result = await runNow()
    expect(result.ok).toBe(false)
    expect(result.error).toContain('500')
  })
})

describe('getPreview', () => {
  const PREVIEW = {
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

  it('fetches /api/pipeline/preview and returns the result', async () => {
    mockFetch(PREVIEW)
    const result = await getPreview()
    expect(result?.recordCount).toBe(2)
    expect(result?.schemaVersion).toBe('2.0')
    expect(result?.records).toHaveLength(2)
    expect(fetch).toHaveBeenCalledWith('/api/pipeline/preview', expect.any(Object))
  })

  it('returns null on non-ok response', async () => {
    mockFetch(null, 401)
    const result = await getPreview()
    expect(result).toBeNull()
  })
})
