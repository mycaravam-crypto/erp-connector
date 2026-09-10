import { describe, it, expect } from 'vitest'
import { findUnmappedRootFields } from '@/lib/importPreviewHints'
import type { ImportNode } from '@/api/importDefinitions'

function scalarField(sourceKey: string, enabled = true): ImportNode {
  return {
    sourceKey,
    kind: 'scalar-field',
    targetColumn: sourceKey,
    relatedTable: null,
    joinKey: null,
    sourceJoinKey: null,
    onMissingChild: 'reject',
    mapping: null,
    enabled,
    children: [],
  }
}

const ROOT: ImportNode = {
  sourceKey: 'root',
  kind: 'root',
  targetColumn: null,
  relatedTable: null,
  joinKey: null,
  sourceJoinKey: null,
  onMissingChild: 'reject',
  mapping: null,
  enabled: true,
  children: [scalarField('guid'), scalarField('status'), scalarField('disabledField', false)],
}

describe('findUnmappedRootFields', () => {
  it('returns fields present in the sample but not mapped as an enabled scalar field', () => {
    const json = JSON.stringify({
      schemaVersion: '1',
      records: [{ guid: 'a', status: 'confirmed', notes: 'unmapped', qty: 3 }],
    })
    expect(findUnmappedRootFields(json, ROOT)).toEqual(['notes', 'qty'])
  })

  it('treats a disabled field as unmapped too', () => {
    const json = JSON.stringify({ schemaVersion: '1', records: [{ disabledField: 'x' }] })
    expect(findUnmappedRootFields(json, ROOT)).toEqual(['disabledField'])
  })

  it('returns [] when every record key is mapped', () => {
    const json = JSON.stringify({ schemaVersion: '1', records: [{ guid: 'a', status: 'confirmed' }] })
    expect(findUnmappedRootFields(json, ROOT)).toEqual([])
  })

  it('returns [] without a rootNode', () => {
    const json = JSON.stringify({ schemaVersion: '1', records: [{ notes: 'x' }] })
    expect(findUnmappedRootFields(json, null)).toEqual([])
  })

  it('returns [] for malformed JSON', () => {
    expect(findUnmappedRootFields('not json', ROOT)).toEqual([])
  })

  it('returns [] when there is no records array', () => {
    expect(findUnmappedRootFields(JSON.stringify({ schemaVersion: '1' }), ROOT)).toEqual([])
  })

  it('de-duplicates unmapped keys across multiple records and sorts them', () => {
    const json = JSON.stringify({
      schemaVersion: '1',
      records: [{ zeta: 1 }, { alpha: 2 }, { zeta: 3 }],
    })
    expect(findUnmappedRootFields(json, ROOT)).toEqual(['alpha', 'zeta'])
  })
})
