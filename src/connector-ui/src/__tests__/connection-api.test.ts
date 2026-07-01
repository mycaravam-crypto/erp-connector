import { describe, it, expect, vi, beforeEach } from 'vitest'
import { getConnection, saveConnection, getSourceSchema, isConnectionConfigured, invalidateConnectionCache } from '@/api/connection'

const SCHEMA = {
  connectionLabel: 'localhost:5432/erp',
  tables: [
    { name: 'systemconfiguration', description: '', columns: [] },
    { name: 'masterdata', description: '', columns: [] },
  ],
}

const CONN_INFO = { host: 'localhost', port: 5432, database: 'erp', username: 'ro' }

function mockFetch(body: unknown, status = 200) {
  return vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
    ok: status >= 200 && status < 300,
    status,
    json: async () => body,
    text: async () => (typeof body === 'string' ? body : JSON.stringify(body)),
  } as Response)
}

beforeEach(() => { vi.restoreAllMocks(); invalidateConnectionCache() })

describe('getConnection', () => {
  it('returns the connection info on 200', async () => {
    mockFetch(CONN_INFO)
    const result = await getConnection()
    expect(result).toEqual(CONN_INFO)
    expect(fetch).toHaveBeenCalledWith('/api/connection', expect.any(Object))
  })

  it('returns null on 404', async () => {
    mockFetch(null, 404)
    const result = await getConnection()
    expect(result).toBeNull()
  })
})

describe('saveConnection', () => {
  it('returns { schema } on success', async () => {
    mockFetch(SCHEMA)
    const result = await saveConnection({ ...CONN_INFO, password: 'secret' })
    expect('schema' in result).toBe(true)
    if ('schema' in result) expect(result.schema).toEqual(SCHEMA)
    expect(fetch).toHaveBeenCalledWith(
      '/api/connection',
      expect.objectContaining({ method: 'POST' }),
    )
  })

  it('sends all fields including password in the body', async () => {
    mockFetch(SCHEMA)
    await saveConnection({ host: 'db.local', port: 5433, database: 'prod', username: 'reader', password: 'pw' })
    const call = vi.mocked(fetch).mock.calls[0]
    const body = JSON.parse(call[1]?.body as string)
    expect(body).toEqual({ host: 'db.local', port: 5433, database: 'prod', username: 'reader', password: 'pw' })
  })

  it('returns { error, status } on 400 with the server message', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: false, status: 400,
      text: async () => 'Connection failed: authentication failed for user "bad"',
    } as Response)
    const result = await saveConnection({ ...CONN_INFO, password: 'bad' })
    expect('error' in result).toBe(true)
    if ('error' in result) {
      expect(result.status).toBe(400)
      expect(result.error).toContain('authentication failed')
    }
  })

  it('returns { error, status: 401 } on expired session', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: false, status: 401,
      text: async () => '',
    } as Response)
    const result = await saveConnection({ ...CONN_INFO, password: 'pw' })
    expect('error' in result).toBe(true)
    if ('error' in result) expect(result.status).toBe(401)
  })
})

describe('isConnectionConfigured', () => {
  it('returns true when GET /api/connection succeeds', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: true, status: 200,
      json: async () => CONN_INFO,
    } as Response)
    expect(await isConnectionConfigured()).toBe(true)
  })

  it('returns false when GET /api/connection returns 404', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: false, status: 404,
      json: async () => null,
    } as Response)
    expect(await isConnectionConfigured()).toBe(false)
  })

  it('caches the result — only one fetch call for two checks', async () => {
    const spy = vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: true, status: 200,
      json: async () => CONN_INFO,
    } as Response)
    await isConnectionConfigured()
    await isConnectionConfigured()
    expect(spy).toHaveBeenCalledTimes(1)
  })

  it('re-fetches after invalidateConnectionCache()', async () => {
    vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce({ ok: true, status: 200, json: async () => CONN_INFO } as Response)
      .mockResolvedValueOnce({ ok: false, status: 404, json: async () => null } as Response)
    await isConnectionConfigured()
    invalidateConnectionCache()
    expect(await isConnectionConfigured()).toBe(false)
  })
})

describe('getSourceSchema', () => {
  it('returns the schema on 200', async () => {
    mockFetch(SCHEMA)
    const result = await getSourceSchema()
    expect(result?.connectionLabel).toBe('localhost:5432/erp')
    expect(result?.tables).toHaveLength(2)
    expect(fetch).toHaveBeenCalledWith('/api/source-schema', expect.any(Object))
  })

  it('returns null on non-ok response', async () => {
    mockFetch(null, 401)
    const result = await getSourceSchema()
    expect(result).toBeNull()
  })
})
