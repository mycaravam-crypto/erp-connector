import { describe, it, expect, vi, beforeEach } from 'vitest'
import { listExports, getExport, releaseExport } from '@/api/exports'

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
    const payload = [{ sequenceNo: 1, status: 'Pending', recordCount: 5, sha256Short: 'abc', extractedAt: '', dataFileName: '' }]
    mockFetch(payload)
    const result = await listExports()
    expect(result).toEqual(payload)
    expect(fetch).toHaveBeenCalledWith('/api/exports', expect.any(Object))
  })
})

describe('getExport', () => {
  it('returns the detail object on 200', async () => {
    const payload = { id: 1, sequenceNo: 1, status: 'Pending', recordCount: 5, sha256: 'full', extractedAt: '', dataFileName: '', releasedAt: null, operatedBy: null, approvedBy: null }
    mockFetch(payload, 200)
    const result = await getExport(1)
    expect(result).toEqual(payload)
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
    const result = await releaseExport(1, { operator: 'alice', approver: 'bob' })
    expect(result.ok).toBe(true)
    expect(result.status).toBe(200)
  })

  it('returns ok:false with message on 400', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: false, status: 400, text: async () => 'Operator und Approver müssen verschiedene Personen sein.',
    } as Response)
    const result = await releaseExport(1, { operator: 'x', approver: 'x' })
    expect(result.ok).toBe(false)
    expect(result.message).toContain('verschiedene')
  })

  it('sends correct JSON body', async () => {
    const spy = vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: true, status: 200, text: async () => '',
    } as Response)
    await releaseExport(3, { operator: 'alice', approver: 'bob' })
    const [url, init] = spy.mock.calls[0]
    expect(url).toBe('/api/exports/3/release')
    expect(init?.method).toBe('POST')
    expect(JSON.parse(init?.body as string)).toEqual({ operator: 'alice', approver: 'bob' })
  })
})
