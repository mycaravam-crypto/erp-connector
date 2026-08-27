import { describe, it, expect, vi, beforeEach } from 'vitest'
import {
  listExportDefinitions,
  getExportDefinition,
  updateExportDefinition,
  testExportDefinition,
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
  const REQUEST = {
    name: DEFINITION.name,
    description: DEFINITION.description,
    rootTable: 'systemconfiguration',
    rootNode: DEFINITION.rootNode,
    outputFormat: DEFINITION.outputFormat,
    isEnabled: DEFINITION.isEnabled,
    schedule: DEFINITION.schedule,
  }

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
