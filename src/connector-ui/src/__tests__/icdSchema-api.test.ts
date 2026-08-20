import { describe, it, expect, vi, beforeEach } from 'vitest'
import { getSchema } from '@/api/icdSchema'

function mockFetch(body: unknown, status = 200) {
  return vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
    ok: status >= 200 && status < 300,
    status,
    json: async () => body,
    text: async () => (typeof body === 'string' ? body : JSON.stringify(body)),
  } as Response)
}

beforeEach(() => vi.restoreAllMocks())

describe('getSchema', () => {
  it('fetches /api/schema and returns the definition', async () => {
    const payload = {
      version: '2.0',
      columns: [
        { name: 'guid', erpSource: 'systemconfiguration.id', type: 'UUID text', notes: 'Coalesce key', active: true },
      ],
    }
    mockFetch(payload)
    const result = await getSchema()
    expect(result).toEqual(payload)
    expect(fetch).toHaveBeenCalledWith('/api/schema', expect.any(Object))
  })

  it('returns null on non-ok response', async () => {
    mockFetch(null, 401)
    const result = await getSchema()
    expect(result).toBeNull()
  })
})
