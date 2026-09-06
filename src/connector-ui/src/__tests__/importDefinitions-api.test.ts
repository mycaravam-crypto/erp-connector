import { describe, it, expect, vi, beforeEach } from 'vitest'
import {
  listImportDefinitions,
  getImportDefinition,
  createImportDefinition,
  updateImportDefinition,
  deleteImportDefinition,
  duplicateImportDefinition,
  setImportDefinitionEnabled,
  previewImportDefinition,
  listImportDefinitionRuns,
  getImportRun,
  releaseImportRun,
  rejectImportRun,
  type ImportDefinition,
} from '@/api/importDefinitions'

function mockFetch(body: unknown, status = 200) {
  return vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
    ok: status >= 200 && status < 300,
    status,
    json: async () => body,
    text: async () => (typeof body === 'string' ? body : JSON.stringify(body)),
  } as Response)
}

beforeEach(() => vi.restoreAllMocks())

const DEFINITION: ImportDefinition = {
  id: 1,
  name: 'Vendor Confirmations',
  description: null,
  rootTable: 'masterdata',
  rootMatchColumn: 'guid',
  unmatchedRootPolicy: 'reject',
  allowedWritableColumns: ['status'],
  isEnabled: false,
  configVersion: 1,
  createdBy: 'alice',
  createdAt: '2026-09-01T00:00:00Z',
  updatedBy: null,
  updatedAt: null,
  rootNode: {
    sourceKey: 'root',
    kind: 'root',
    targetColumn: null,
    relatedTable: null,
    joinKey: null,
    sourceJoinKey: null,
    onMissingChild: 'reject',
    mapping: null,
    enabled: true,
    children: [
      {
        sourceKey: 'guid',
        kind: 'scalar-field',
        targetColumn: 'guid',
        relatedTable: null,
        joinKey: null,
        sourceJoinKey: null,
        onMissingChild: 'reject',
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
  rootTable: DEFINITION.rootTable,
  rootMatchColumn: DEFINITION.rootMatchColumn,
  rootNode: DEFINITION.rootNode,
  allowedWritableColumns: DEFINITION.allowedWritableColumns,
  unmatchedRootPolicy: DEFINITION.unmatchedRootPolicy,
  isEnabled: DEFINITION.isEnabled,
}

describe('listImportDefinitions', () => {
  it('returns the summary list on 200', async () => {
    mockFetch([DEFINITION])
    const result = await listImportDefinitions()
    expect(result).toEqual([DEFINITION])
    expect(fetch).toHaveBeenCalledWith('/api/import-definitions', expect.any(Object))
  })

  it('returns an empty array on a non-ok response', async () => {
    mockFetch(null, 401)
    expect(await listImportDefinitions()).toEqual([])
  })
})

describe('getImportDefinition', () => {
  it('returns the definition on 200', async () => {
    mockFetch(DEFINITION)
    const result = await getImportDefinition(1)
    expect(result).toEqual(DEFINITION)
    expect(fetch).toHaveBeenCalledWith('/api/import-definitions/1', expect.any(Object))
  })

  it('returns null on a non-ok response', async () => {
    mockFetch(null, 404)
    expect(await getImportDefinition(1)).toBeNull()
  })
})

describe('createImportDefinition', () => {
  it('POSTs the request and returns the created definition', async () => {
    mockFetch(DEFINITION, 201)
    const result = await createImportDefinition(REQUEST)
    expect(result).toEqual({ ok: true, data: DEFINITION })
    expect(fetch).toHaveBeenCalledWith(
      '/api/import-definitions',
      expect.objectContaining({ method: 'POST', body: JSON.stringify(REQUEST) }),
    )
  })
})

describe('updateImportDefinition', () => {
  it('PUTs the request and returns the saved definition on success', async () => {
    mockFetch({ ...DEFINITION, rootTable: 'item' })
    const result = await updateImportDefinition(1, REQUEST)
    expect(result).toEqual({ ok: true, data: { ...DEFINITION, rootTable: 'item' } })
    expect(fetch).toHaveBeenCalledWith(
      '/api/import-definitions/1',
      expect.objectContaining({ method: 'PUT', body: JSON.stringify(REQUEST) }),
    )
  })

  it('returns the server error detail on validation failure', async () => {
    mockFetch('RootMatchColumn is required and must be a valid identifier.', 400)
    const result = await updateImportDefinition(1, REQUEST)
    expect(result).toEqual({
      ok: false,
      error: 'RootMatchColumn is required and must be a valid identifier.',
    })
  })
})

describe('deleteImportDefinition', () => {
  it('returns true on a successful delete', async () => {
    mockFetch(null, 204)
    expect(await deleteImportDefinition(1)).toBe(true)
    expect(fetch).toHaveBeenCalledWith('/api/import-definitions/1', expect.objectContaining({ method: 'DELETE' }))
  })

  it('returns false when the definition does not exist', async () => {
    mockFetch(null, 404)
    expect(await deleteImportDefinition(1)).toBe(false)
  })
})

describe('duplicateImportDefinition', () => {
  it('POSTs to /duplicate and returns the copy', async () => {
    mockFetch({ ...DEFINITION, id: 2, name: 'Vendor Confirmations (Copy)' }, 201)
    const result = await duplicateImportDefinition(1)
    expect(result.ok).toBe(true)
    expect(fetch).toHaveBeenCalledWith(
      '/api/import-definitions/1/duplicate',
      expect.objectContaining({ method: 'POST', body: JSON.stringify({}) }),
    )
  })
})

describe('setImportDefinitionEnabled', () => {
  it('PATCHes /enable with the requested state', async () => {
    mockFetch({ ...DEFINITION, isEnabled: true })
    const result = await setImportDefinitionEnabled(1, true)
    expect(result).toEqual({ ok: true, data: { ...DEFINITION, isEnabled: true } })
    expect(fetch).toHaveBeenCalledWith(
      '/api/import-definitions/1/enable',
      expect.objectContaining({ method: 'PATCH', body: JSON.stringify({ enabled: true }) }),
    )
  })
})

describe('previewImportDefinition', () => {
  it('POSTs the sample JSON to /preview and returns the computed plan', async () => {
    const plan = {
      recordCount: 1,
      matchedCount: 1,
      changedCount: 1,
      unchangedCount: 0,
      rejectedCount: 0,
      invalidCount: 0,
      operations: [
        {
          correlationValue: 'abc',
          table: 'masterdata',
          keyColumn: 'guid',
          keyValue: 'abc',
          column: 'status',
          expectedOldValue: 'pending',
          newValue: 'confirmed',
        },
      ],
    }
    mockFetch(plan)
    const result = await previewImportDefinition(1, '{"schemaVersion":1,"records":[]}')
    expect(result).toEqual({ ok: true, data: plan })
    expect(fetch).toHaveBeenCalledWith(
      '/api/import-definitions/1/preview',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ inboundJson: '{"schemaVersion":1,"records":[]}' }),
      }),
    )
  })

  it('returns the server error detail on failure', async () => {
    mockFetch('Unknown or missing schemaVersion.', 400)
    const result = await previewImportDefinition(1, '{}')
    expect(result).toEqual({ ok: false, error: 'Unknown or missing schemaVersion.' })
  })
})

describe('listImportDefinitionRuns', () => {
  it('returns the run history on 200', async () => {
    const runs = [
      {
        id: 1,
        configVersion: 1,
        startedAt: '2026-09-01T00:00:00Z',
        finishedAt: '2026-09-01T00:00:01Z',
        status: 'PendingReview',
        recordCount: 3,
        matchedCount: 3,
        changedCount: 2,
        unchangedCount: 1,
        rejectedCount: 0,
        conflictCount: 0,
        invalidCount: 0,
        errorMessage: null,
        triggeredBy: 'watcher',
        operatedBy: null,
        approvedBy: null,
        releasedAt: null,
      },
    ]
    mockFetch(runs)
    expect(await listImportDefinitionRuns(1)).toEqual(runs)
    expect(fetch).toHaveBeenCalledWith('/api/import-definitions/1/runs', expect.any(Object))
  })

  it('returns an empty array on a non-ok response', async () => {
    mockFetch(null, 404)
    expect(await listImportDefinitionRuns(1)).toEqual([])
  })
})

describe('getImportRun', () => {
  it('returns the run detail on 200', async () => {
    const detail = {
      id: 5,
      importDefinitionId: 1,
      importDefinitionName: 'Vendor Confirmations',
      configVersion: 1,
      sourceFileName: 'vendor-2026-09-01.json',
      startedAt: '2026-09-01T00:00:00Z',
      finishedAt: '2026-09-01T00:00:01Z',
      status: 'PendingReview',
      recordCount: 1,
      matchedCount: 1,
      changedCount: 1,
      unchangedCount: 0,
      rejectedCount: 0,
      conflictCount: 0,
      invalidCount: 0,
      errorMessage: null,
      triggeredBy: 'watcher',
      operatedBy: null,
      approvedBy: null,
      releasedAt: null,
      operations: [],
    }
    mockFetch(detail)
    expect(await getImportRun(5)).toEqual(detail)
    expect(fetch).toHaveBeenCalledWith('/api/import-runs/5', expect.any(Object))
  })

  it('returns null on a non-ok response', async () => {
    mockFetch(null, 404)
    expect(await getImportRun(5)).toBeNull()
  })
})

describe('releaseImportRun', () => {
  it('POSTs the approver and returns the released run', async () => {
    const run = {
      id: 5,
      importDefinitionId: 1,
      status: 'Released',
      recordCount: 1,
      matchedCount: 1,
      changedCount: 1,
      unchangedCount: 0,
      rejectedCount: 0,
      conflictCount: 0,
      invalidCount: 0,
      operatedBy: 'alice',
      approvedBy: 'bob',
      releasedAt: '2026-09-01T00:05:00Z',
      errorMessage: null,
    }
    mockFetch(run)
    const result = await releaseImportRun(5, 'bob')
    expect(result).toEqual({ ok: true, data: run })
    expect(fetch).toHaveBeenCalledWith(
      '/api/import-runs/5/release',
      expect.objectContaining({ method: 'POST', body: JSON.stringify({ approver: 'bob' }) }),
    )
  })

  it('returns the server error on a conflict', async () => {
    mockFetch('Import run #5 is already Released.', 409)
    const result = await releaseImportRun(5, 'bob')
    expect(result).toEqual({ ok: false, error: 'Import run #5 is already Released.' })
  })
})

describe('rejectImportRun', () => {
  it('POSTs to /reject and returns the rejected run', async () => {
    mockFetch({
      id: 5,
      importDefinitionId: 1,
      status: 'Rejected',
      recordCount: 1,
      matchedCount: 1,
      changedCount: 1,
      unchangedCount: 0,
      rejectedCount: 0,
      conflictCount: 0,
      invalidCount: 0,
      operatedBy: 'alice',
      approvedBy: null,
      releasedAt: null,
      errorMessage: null,
    })
    const result = await rejectImportRun(5)
    expect(result.ok).toBe(true)
    expect(fetch).toHaveBeenCalledWith('/api/import-runs/5/reject', expect.objectContaining({ method: 'POST' }))
  })
})
