/** The ImportEnvelope schemaVersion this frontend targets — mirrors
 * Connector.Infrastructure.ImportNodeWalker.SupportedSchemaVersion (import-definitions.md Open Decision
 * #14). A literal, not fetched from the backend: it's part of the wire contract, not runtime state. */
export const IMPORT_ENVELOPE_SCHEMA_VERSION = '1'

/** sessionStorage key used to hand a pasted export sample from the Preview panel of an *existing* Import
 * Definition to the "New Import Definition" suggestion panel across a route navigation — see
 * ImportDefinitionPreviewPanel.vue's "Create Import Definition from this export" action and
 * ImportMappingSuggestionPanel.vue's prefill-on-mount. Read once and removed immediately, so it never
 * leaks into an unrelated later visit to the New Import Definition flow. */
export const EXPORT_SAMPLE_HANDOFF_KEY = 'erp-connector:import-mapping-suggestion-prefill'

export interface DetectedExportFile {
  records: unknown[]
  provenance: unknown | null
}

/** Detects the specific confusion Connector.Infrastructure.ImportNodeWalker.ParseRecords now names
 * explicitly server-side (its schema_version/schemaVersion branch, added alongside this): a pasted file
 * carrying "schema_version" (snake_case) but no "schemaVersion" is the connector's own export output
 * (JsonExportFormatWriter), not an ImportEnvelope — an unrelated shape that happens to share
 * "records"/"provenance" vocabulary. Returns null for anything else (not JSON, already envelope-shaped,
 * or some other malformed input) — detection must never guess at a shape it doesn't recognize; an
 * unrelated parse/shape problem still surfaces through the normal Preview error path. */
export function detectExportFile(raw: string): DetectedExportFile | null {
  let parsed: unknown
  try {
    parsed = JSON.parse(raw)
  } catch {
    return null
  }
  if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) return null

  const obj = parsed as Record<string, unknown>
  if ('schemaVersion' in obj) return null
  if (!('schema_version' in obj)) return null

  const records = obj.records
  if (!Array.isArray(records)) return null

  return { records, provenance: 'provenance' in obj ? obj.provenance : null }
}

/** Rewraps a detected export file as a minimal ImportEnvelope: the same records array, the same
 * provenance block carried through untouched (ImportNodeWalker ignores it entirely — see the Slice 3
 * regression test WalkAsync_ProvenanceBlockOnEnvelope_HasNoEffectOnTheWalk), just the wrapper the walker
 * actually reads. Never touches AllowedWritableColumns or field data — a pure reshape of already-pasted
 * JSON, not a new trust boundary. */
export function toImportEnvelope(detected: DetectedExportFile): string {
  const envelope: Record<string, unknown> = {
    schemaVersion: IMPORT_ENVELOPE_SCHEMA_VERSION,
    records: detected.records,
  }
  if (detected.provenance !== null) envelope.provenance = detected.provenance
  return JSON.stringify(envelope, null, 2)
}

/** True when a detected export's provenance block carries a non-empty integrationKey — the signal that
 * "Create Import Definition from this export" (import-mapping-presets.md §3.4) has anything to work
 * with. This never claims a match itself: ImportMappingSuggestion.Evaluate still does the real
 * (IntegrationKey, ContractVersion) lookup server-side once the operator follows through. */
export function hasIntegrationKeyProvenance(detected: DetectedExportFile): boolean {
  const p = detected.provenance
  if (typeof p !== 'object' || p === null) return false
  const key = (p as Record<string, unknown>).integrationKey
  return typeof key === 'string' && key !== ''
}
