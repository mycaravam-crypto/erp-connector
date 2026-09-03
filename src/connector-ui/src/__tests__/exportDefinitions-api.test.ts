import { describe, it, expect, vi, beforeEach } from 'vitest'
import {
  listExportDefinitions,
  getExportDefinition,
  updateExportDefinition,
  testExportDefinition,
  createExportDefinition,
  deleteExportDefinition,
  duplicateExportDefinition,
  setExportDefinitionEnabled,
  previewExportDefinition,
  listExportDefinitionRuns,
  runExportDefinition,
  type ExportDefinition,
} from '@/api/exportDefinitions'

function mockFetch(body: unknown, status = 200) {
  return vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
    ok: status >= 200 && status < 300,
    status,
    json: async () => body,
    text: async () => (typeof body === 'string' ? body : JSON.stringify(body)),
  } as Response)
}

beforeEach(() => vi.restoreAllMocks())

const DEFINITION: ExportDefinition = {
  id: 1,
  name: 'Legacy Export',
  description: null,
  rootTable: 'masterdata',
  outputFormat: 'json',
  isEnabled: false,
  schedule: null,
  configVersion: 1,
  createdBy: 'migration',
  createdAt: '2026-08-01T00:00:00Z',
  updatedBy: null,
  updatedAt: null,
  rootNode: {
    targetKey: 'root',
    kind: 'root',
    sourceField: null,
    relatedTable: null,
    joinKey: null,
    sourceJoinKey: null,
    filter: null,
    mapping: null,
    enabled: true,
    children: [
      {
        targetKey: 'part_number',
        kind: 'scalar-field',
        sourceField: 'part_number',
        relatedTable: null,
        joinKey: null,
        sourceJoinKey: null,
        filter: null,
        mapping: null,
        enabled: true,
        children: [],
      },
    ],
  },
}

const REQUEST = {
  name: DEFINITION.name,
  description: DEFINITION.description,
  rootTable: 'systemconfiguration',
  rootNode: DEFINITION.rootNode,
  outputFormat: DEFINITION.outputFormat,
  isEnabled: DEFINITION.isEnabled,
  schedule: DEFINITION.schedule,
}

describe('listExportDefinitions', () => {
  it('returns the summary list on 200', async () => {
    mockFetch([DEFINITION])
    const result = await listExportDefinitions()
    expect(result).toEqual([DEFINITION])
    expect(fetch).toHaveBeenCalledWith('/api/export-definitions', expect.any(Object))
  })

  it('returns an empty array on a non-ok response', async () => {
    mockFetch(null, 401)
    expect(await listExportDefinitions()).toEqual([])
  })
})

describe('getExportDefinition', () => {
  it('returns the definition on 200', async () => {
    mockFetch(DEFINITION)
    const result = await getExportDefinition(1)
    expect(result).toEqual(DEFINITION)
    expect(fetch).toHaveBeenCalledWith('/api/export-definitions/1', expect.any(Object))
  })

  it('returns null on a non-ok response', async () => {
    mockFetch(null, 404)
    expect(await getExportDefinition(1)).toBeNull()
  })
})

describe('updateExportDefinition', () => {
  it('PUTs the request and returns the saved definition on success', async () => {
    mockFetch({ ...DEFINITION, rootTable: 'systemconfiguration' })
    const result = await updateExportDefinition(1, REQUEST)
    expect(result).toEqual({ ok: true, data: { ...DEFINITION, rootTable: 'systemconfiguration' } })
    expect(fetch).toHaveBeenCalledWith(
      '/api/export-definitions/1',
      expect.objectContaining({ method: 'PUT', body: JSON.stringify(REQUEST) }),
    )
  })

  it('returns the server error detail on validation failure', async () => {
    mockFetch('RootTable is required and must be a valid identifier.', 400)
    const result = await updateExportDefinition(1, REQUEST)
    expect(result).toEqual({
      ok: false,
      error: 'RootTable is required and must be a valid identifier.',
    })
  })
})

describe('testExportDefinition', () => {
  it('POSTs to /test and returns the run result on success', async () => {
    mockFetch({
      runId: 5,
      status: 'Success',
      recordCount: 3,
      configVersion: 1,
      startedAt: '2026-08-27T00:00:00Z',
      finishedAt: '2026-08-27T00:00:01Z',
      errorMessage: null,
      isTestRun: true,
    })
    const result = await testExportDefinition(1)
    expect(result.ok).toBe(true)
    expect(fetch).toHaveBeenCalledWith('/api/export-definitions/1/test', expect.objectContaining({ method: 'POST' }))
  })

  it('returns the server error detail on failure', async () => {
    mockFetch('relation "masterdata" does not exist', 500)
    const result = await testExportDefinition(1)
    expect(result).toEqual({ ok: false, error: 'relation "masterdata" does not exist' })
  })
})

describe('createExportDefinition', () => {
  it('POSTs the request and returns the created definition', async () => {
    mockFetch(DEFINITION, 201)
    const result = await createExportDefinition(REQUEST)
    expect(result).toEqual({ ok: true, data: DEFINITION })
    expect(fetch).toHaveBeenCalledWith(
      '/api/export-definitions',
      expect.objectContaining({ method: 'POST', body: JSON.stringify(REQUEST) }),
    )
  })
})

describe('deleteExportDefinition', () => {
  it('returns true on a successful delete', async () => {
    mockFetch(null, 204)
    expect(await deleteExportDefinition(1)).toBe(true)
    expect(fetch).toHaveBeenCalledWith('/api/export-definitions/1', expect.objectContaining({ method: 'DELETE' }))
  })

  it('returns false when the definition does not exist', async () => {
    mockFetch(null, 404)
    expect(await deleteExportDefinition(1)).toBe(false)
  })
})

describe('duplicateExportDefinition', () => {
  it('POSTs to /duplicate and returns the copy', async () => {
    mockFetch({ ...DEFINITION, id: 2, name: 'Legacy Export (Copy)' }, 201)
    const result = await duplicateExportDefinition(1)
    expect(result.ok).toBe(true)
    expect(fetch).toHaveBeenCalledWith(
      '/api/export-definitions/1/duplicate',
      expect.objectContaining({ method: 'POST', body: JSON.stringify({}) }),
    )
  })
})

describe('setExportDefinitionEnabled', () => {
  it('PATCHes /enable with the requested state', async () => {
    mockFetch({ ...DEFINITION, isEnabled: true })
    const result = await setExportDefinitionEnabled(1, true)
    expect(result).toEqual({ ok: true, data: { ...DEFINITION, isEnabled: true } })
    expect(fetch).toHaveBeenCalledWith(
      '/api/export-definitions/1/enable',
      expect.objectContaining({ method: 'PATCH', body: JSON.stringify({ enabled: true }) }),
    )
  })
})

describe('previewExportDefinition', () => {
  it('POSTs to /preview and returns capped records', async () => {
    mockFetch({ recordCount: 2, records: [{ a: 1 }, { a: 2 }] })
    const result = await previewExportDefinition(1)
    expect(result).toEqual({ ok: true, data: { recordCount: 2, records: [{ a: 1 }, { a: 2 }] } })
    expect(fetch).toHaveBeenCalledWith(
      '/api/export-definitions/1/preview',
      expect.objectContaining({ method: 'POST' }),
    )
  })
})

describe('listExportDefinitionRuns', () => {
  it('returns the run history on 200', async () => {
    const runs = [
      {
        id: 1,
        configVersion: 1,
        startedAt: '2026-08-27T00:00:00Z',
        finishedAt: '2026-08-27T00:00:01Z',
        status: 'Success',
        recordCount: 3,
        errorMessage: null,
        triggeredBy: 'scheduler',
        isTestRun: false,
      },
    ]
    mockFetch(runs)
    expect(await listExportDefinitionRuns(1)).toEqual(runs)
    expect(fetch).toHaveBeenCalledWith('/api/export-definitions/1/runs', expect.any(Object))
  })

  it('returns an empty array on a non-ok response', async () => {
    mockFetch(null, 404)
    expect(await listExportDefinitionRuns(1)).toEqual([])
  })
})

describe('runExportDefinition', () => {
  it('returns the artifact blob, file name, and record count on success', async () => {
    const blob = new Blob(['a,b\n1,2'], { type: 'text/csv' })
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: true,
      status: 200,
      headers: new Headers({
        'Content-Disposition': 'attachment; filename="export.csv"',
        'X-Record-Count': '3',
      }),
      blob: async () => blob,
    } as unknown as Response)

    const result = await runExportDefinition(1)
    expect(result).toEqual({ ok: true, blob, fileName: 'export.csv', recordCount: 3 })
  })

  it('returns the server error detail on failure', async () => {
    mockFetch('No database connection configured.', 500)
    const result = await runExportDefinition(1)
    expect(result).toEqual({ ok: false, error: 'No database connection configured.' })
  })
})
