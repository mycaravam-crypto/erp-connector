import { describe, it, expect } from 'vitest'
import {
  detectExportFile,
  hasIntegrationKeyProvenance,
  toImportEnvelope,
  IMPORT_ENVELOPE_SCHEMA_VERSION,
} from '@/lib/exportedFileDetection'

const EXPORTED_JOB = JSON.stringify({
  schema_version: '2.0',
  extracted_at: '2026-09-10T09:14:45.7780464+00:00',
  provenance: { integrationKey: 'ci-conf', contractVersion: 11, configVersion: 10 },
  records: [{ id: '1', barcode: 'm100000074' }],
})

describe('detectExportFile', () => {
  it('detects an exported job file (schema_version, no schemaVersion)', () => {
    const detected = detectExportFile(EXPORTED_JOB)
    expect(detected).not.toBeNull()
    expect(detected?.records).toEqual([{ id: '1', barcode: 'm100000074' }])
    expect(detected?.provenance).toEqual({ integrationKey: 'ci-conf', contractVersion: 11, configVersion: 10 })
  })

  it('detects an exported job file with no provenance block', () => {
    const detected = detectExportFile(JSON.stringify({ schema_version: '1.0', records: [] }))
    expect(detected).toEqual({ records: [], provenance: null })
  })

  it('does not flag an already-valid ImportEnvelope', () => {
    expect(detectExportFile(JSON.stringify({ schemaVersion: '1', records: [] }))).toBeNull()
  })

  it('does not flag a file with schemaVersion present alongside schema_version', () => {
    expect(
      detectExportFile(JSON.stringify({ schemaVersion: '1', schema_version: '2.0', records: [] })),
    ).toBeNull()
  })

  it('returns null for malformed JSON', () => {
    expect(detectExportFile('not json')).toBeNull()
  })

  it('returns null for a bare JSON array', () => {
    expect(detectExportFile('[]')).toBeNull()
  })

  it('returns null when schema_version is present but records is missing or not an array', () => {
    expect(detectExportFile(JSON.stringify({ schema_version: '2.0' }))).toBeNull()
    expect(detectExportFile(JSON.stringify({ schema_version: '2.0', records: 'oops' }))).toBeNull()
  })
})

describe('toImportEnvelope', () => {
  it('rewraps records and provenance under a valid ImportEnvelope', () => {
    const detected = detectExportFile(EXPORTED_JOB)!
    const envelope = JSON.parse(toImportEnvelope(detected))
    expect(envelope.schemaVersion).toBe(IMPORT_ENVELOPE_SCHEMA_VERSION)
    expect(envelope.records).toEqual(detected.records)
    expect(envelope.provenance).toEqual(detected.provenance)
  })

  it('omits provenance entirely when the export sample had none', () => {
    const detected = detectExportFile(JSON.stringify({ schema_version: '1.0', records: [] }))!
    const envelope = JSON.parse(toImportEnvelope(detected))
    expect(envelope).toEqual({ schemaVersion: IMPORT_ENVELOPE_SCHEMA_VERSION, records: [] })
  })

  it('round-trips back through detectExportFile as a non-export file', () => {
    const detected = detectExportFile(EXPORTED_JOB)!
    expect(detectExportFile(toImportEnvelope(detected))).toBeNull()
  })
})

describe('hasIntegrationKeyProvenance', () => {
  it('is true when provenance carries a non-empty integrationKey', () => {
    const detected = detectExportFile(EXPORTED_JOB)!
    expect(hasIntegrationKeyProvenance(detected)).toBe(true)
  })

  it('is false when there is no provenance block', () => {
    const detected = detectExportFile(JSON.stringify({ schema_version: '1.0', records: [] }))!
    expect(hasIntegrationKeyProvenance(detected)).toBe(false)
  })

  it('is false when provenance has no integrationKey', () => {
    const detected = detectExportFile(
      JSON.stringify({ schema_version: '1.0', provenance: { configVersion: 3 }, records: [] }),
    )!
    expect(hasIntegrationKeyProvenance(detected)).toBe(false)
  })

  it('is false when integrationKey is an empty string', () => {
    const detected = detectExportFile(
      JSON.stringify({ schema_version: '1.0', provenance: { integrationKey: '' }, records: [] }),
    )!
    expect(hasIntegrationKeyProvenance(detected)).toBe(false)
  })
})
