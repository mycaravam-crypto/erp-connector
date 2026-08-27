import { describe, it, expect, vi, beforeEach } from 'vitest'
import {
  getExportMapping, saveExportMapping,
  getPresets, savePreset, deletePreset,
} from '@/api/mapping'
import type { ExportMappingConfig } from '@/api/mapping'

function mockFetch(body: unknown, status = 200) {
  return vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
    ok: status >= 200 && status < 300,
    status,
    json: async () => body,
    text: async () => (typeof body === 'string' ? body : JSON.stringify(body)),
  } as Response)
}

beforeEach(() => vi.restoreAllMocks())

const PRESET_CONFIG: ExportMappingConfig = {
  sourceTable: 'parts',
  fields: [
    { sourceName: 'id', targetName: 'id', enabled: true },
    { sourceName: 'part_name', targetName: 'part_name', enabled: true },
  ],
  relations: [],
  nestedGroups: [],
  jsonWrapper: null,
}

describe('getExportMapping', () => {
  const MAPPING: ExportMappingConfig = {
    sourceTable: 'parts',
    fields: [{ sourceName: 'id', targetName: 'id', enabled: true }],
    relations: [],
    nestedGroups: [],
    jsonWrapper: null,
  }

  it('returns the mapping config on 200', async () => {
    mockFetch(MAPPING)
    const result = await getExportMapping()
    expect(result).toEqual(MAPPING)
    expect(fetch).toHaveBeenCalledWith('/api/export-mapping', expect.any(Object))
  })

  it('returns null on non-ok response', async () => {
    mockFetch(null, 404)
    const result = await getExportMapping()
    expect(result).toBeNull()
  })
})

describe('saveExportMapping', () => {
  const CONFIG: ExportMappingConfig = {
    sourceTable: 'systemconfiguration',
    fields: [
      { sourceName: 'id',     targetName: 'guid',   enabled: true },
      { sourceName: 'serial', targetName: 'serial', enabled: false },
    ],
    relations: [],
    nestedGroups: [],
    jsonWrapper: null,
  }

  it('returns { ok: true, data } on 200', async () => {
    mockFetch(CONFIG)
    const result = await saveExportMapping(CONFIG)
    expect(result.ok).toBe(true)
    expect(result.data).toEqual(CONFIG)
    expect(fetch).toHaveBeenCalledWith(
      '/api/export-mapping',
      expect.objectContaining({ method: 'PUT' }),
    )
  })

  it('sends the correct JSON body', async () => {
    const spy = mockFetch(CONFIG)
    await saveExportMapping(CONFIG)
    const body = JSON.parse(spy.mock.calls[0][1]?.body as string)
    expect(body.sourceTable).toBe('systemconfiguration')
    expect(body.fields).toHaveLength(2)
  })

  it('returns { ok: false, error } on 400', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: false, status: 400,
      text: async () => 'SourceTable is required.',
      json: async () => ({}),
    } as Response)
    const result = await saveExportMapping({ ...CONFIG, sourceTable: '' })
    expect(result.ok).toBe(false)
    expect(result.error).toContain('SourceTable')
  })

  it('extracts the "detail" field from a problem+json error instead of dumping the raw envelope', async () => {
    mockFetch(
      {
        type: 'https://tools.ietf.org/html/rfc9110#section-15.5.6',
        title: 'Method Not Allowed',
        status: 405,
        detail:
          'The legacy export-mapping config is read-only. Configure and trigger exports via /api/export-definitions instead.',
      },
      405,
    )
    const result = await saveExportMapping(CONFIG)
    expect(result.ok).toBe(false)
    expect(result.error).toBe(
      'The legacy export-mapping config is read-only. Configure and trigger exports via /api/export-definitions instead.',
    )
  })
})

describe('getPresets', () => {
  it('returns the preset dictionary on 200', async () => {
    const payload = { 'My Preset': PRESET_CONFIG }
    mockFetch(payload)
    const result = await getPresets()
    expect(result).toEqual(payload)
    expect(fetch).toHaveBeenCalledWith('/api/export-mapping/presets', expect.any(Object))
  })

  it('returns an empty object on non-ok response', async () => {
    mockFetch(null, 500)
    expect(await getPresets()).toEqual({})
  })
})

describe('savePreset', () => {
  it('returns { ok: true } on 200', async () => {
    mockFetch(PRESET_CONFIG, 200)
    const result = await savePreset('My Preset', PRESET_CONFIG)
    expect(result).toEqual({ ok: true })
    expect(fetch).toHaveBeenCalledWith(
      '/api/export-mapping/presets/My%20Preset',
      expect.objectContaining({ method: 'PUT' }),
    )
  })

  it('returns { ok: false, error } on 400', async () => {
    mockFetch('SourceTable is required.', 400)
    const result = await savePreset('Bad', { ...PRESET_CONFIG, sourceTable: '' })
    expect(result.ok).toBe(false)
    expect(result.error).toContain('SourceTable')
  })

  it('extracts the "detail" field from a problem+json error', async () => {
    mockFetch({ title: 'Method Not Allowed', status: 405, detail: 'read-only' }, 405)
    const result = await savePreset('My Preset', PRESET_CONFIG)
    expect(result).toEqual({ ok: false, error: 'read-only' })
  })
})

describe('deletePreset', () => {
  it('returns { ok: true } on 204', async () => {
    mockFetch(null, 204)
    const result = await deletePreset('My Preset')
    expect(result).toEqual({ ok: true })
    expect(fetch).toHaveBeenCalledWith(
      '/api/export-mapping/presets/My%20Preset',
      expect.objectContaining({ method: 'DELETE' }),
    )
  })

  it('returns { ok: false } on 404', async () => {
    mockFetch(null, 404)
    const result = await deletePreset('Ghost')
    expect(result.ok).toBe(false)
  })
})
