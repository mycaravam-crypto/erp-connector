import { describe, it, expect, vi, beforeEach } from 'vitest'
import { listExports, getExport, releaseExport, deliverExport, skipExport } from '@/api/exports'

function mockFetch(body: unknown, status = 200) {
  return vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
    ok: status >= 200 && status < 300,
    status,
    json: async () => body,
    text: async () => String(body),
  } as Response)
}

beforeEach(() => vi.restoreAllMocks())

describe('listExports', () => {
  it('fetches /api/exports and returns the array', async () => {
    const payload = [
      {
        sequenceNo: 1, status: 'Pending', recordCount: 5, sha256Short: 'abc',
        extractedAt: '', dataFileName: '', isStale: false,
      },
    ]
    mockFetch(payload)
    const result = await listExports()
    expect(result).toEqual(payload)
    expect(fetch).toHaveBeenCalledWith('/api/exports', expect.any(Object))
  })
})

describe('getExport', () => {
  const detail = {
    id: 1, sequenceNo: 1, status: 'Pending', recordCount: 5, sha256: 'full',
    extractedAt: '', dataFileName: '', releasedAt: null, operatedBy: null, approvedBy: null,
    deliveredAt: null, deliveredBy: null, importedRecordCount: null, deliveryNotes: null,
    sequenceGapWarning: null,
  }

  it('returns the detail object on 200', async () => {
    mockFetch(detail, 200)
    const result = await getExport(1)
    expect(result).toEqual(detail)
  })

  it('returns null on 404', async () => {
    mockFetch(null, 404)
    const result = await getExport(999)
    expect(result).toBeNull()
  })
})

describe('releaseExport', () => {
  it('returns ok:true on 200', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: true, status: 200, text: async () => '',
    } as Response)
    const result = await releaseExport(1, { approver: 'bob' })
    expect(result.ok).toBe(true)
    expect(result.status).toBe(200)
  })

  it('returns ok:false with message on 400', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: false, status: 400, text: async () => 'Operator und Approver müssen verschiedene Personen sein.',
    } as Response)
    const result = await releaseExport(1, { approver: 'alice' })
    expect(result.ok).toBe(false)
    expect(result.message).toContain('verschiedene')
  })

  it('sends correct JSON body (approver only — operator inferred from JWT)', async () => {
    const spy = vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: true, status: 200, text: async () => '',
    } as Response)
    await releaseExport(3, { approver: 'bob' })
    const [url, init] = spy.mock.calls[0]
    expect(url).toBe('/api/exports/3/release')
    expect(init?.method).toBe('POST')
    expect(JSON.parse(init?.body as string)).toEqual({ approver: 'bob' })
  })
})

describe('skipExport', () => {
  it('returns ok:true on 200', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: true, status: 200, text: async () => '',
    } as Response)
    const result = await skipExport(2, { reason: 'ERP offline' })
    expect(result.ok).toBe(true)
  })

  it('returns ok:false with message on 409 (wrong status)', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: false, status: 409, text: async () => "Run #2 has status 'Released' and cannot be skipped.",
    } as Response)
    const result = await skipExport(2, {})
    expect(result.ok).toBe(false)
    expect(result.message).toContain('Released')
  })

  it('sends correct JSON body to POST …/skip', async () => {
    const spy = vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: true, status: 200, text: async () => '',
    } as Response)
    await skipExport(3, { reason: 'lost file' })
    const [url, init] = spy.mock.calls[0]
    expect(url).toBe('/api/exports/3/skip')
    expect(init?.method).toBe('POST')
    expect(JSON.parse(init?.body as string)).toEqual({ reason: 'lost file' })
  })
})

describe('deliverExport', () => {
  it('returns ok:true on 200', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: true, status: 200, text: async () => '',
    } as Response)
    const result = await deliverExport(1, { importedRecordCount: 5, notes: 'USB-007' })
    expect(result.ok).toBe(true)
  })

  it('returns ok:false with message on 400', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: false, status: 400, text: async () => 'Only released runs can be marked as delivered.',
    } as Response)
    const result = await deliverExport(1, {})
    expect(result.ok).toBe(false)
    expect(result.message).toContain('released')
  })

  it('sends correct JSON body to POST …/deliver', async () => {
    const spy = vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: true, status: 200, text: async () => '',
    } as Response)
    await deliverExport(5, { importedRecordCount: 3, notes: 'ref-42' })
    const [url, init] = spy.mock.calls[0]
    expect(url).toBe('/api/exports/5/deliver')
    expect(init?.method).toBe('POST')
    expect(JSON.parse(init?.body as string)).toEqual({ importedRecordCount: 3, notes: 'ref-42' })
  })
})
