import { getToken } from './auth'

function authHeaders(): Record<string, string> {
  const token = getToken()
  const headers: Record<string, string> = { 'Content-Type': 'application/json' }
  if (token) headers['Authorization'] = `Bearer ${token}`
  return headers
}

// ── Dynamic export mapping ────────────────────────────────────────────────────

export interface MappingField {
  sourceName: string
  targetName: string
  enabled: boolean
}

export interface MappingRelationField {
  sourceField: string
  targetField: string
  enabled: boolean
}

export interface MappingRelation {
  relatedTable: string
  joinKey: string
  sourceJoinKey: string
  enabled: boolean
  flattenStrategy: 'string_join' | 'array'
  delimiter: string
  fields: MappingRelationField[]
}

// ── Nested JSON structure (JSON export only) ──────────────────────────────────

export interface MappingNestedField {
  sourceField: string
  targetKey: string
  enabled: boolean
}

export interface MappingNestedGroup {
  targetKey: string
  relatedTable: string
  joinKey: string
  sourceJoinKey: string
  enabled: boolean
  kind: 'object' | 'array'
  fields: MappingNestedField[]
  children: MappingNestedGroup[]
}

export interface ExportJsonMetadataField {
  key: string
  value: string
  isDynamicTimestamp: boolean
}

export interface ExportJsonWrapperConfig {
  rootKey: string
  itemsKey: string
  metadataKey: string
  metadataFields: ExportJsonMetadataField[]
}

export interface ExportMappingConfig {
  sourceTable: string
  fields: MappingField[]
  relations: MappingRelation[]
  nestedGroups: MappingNestedGroup[]
  jsonWrapper: ExportJsonWrapperConfig | null
}

/** Returns the stored export mapping config, or null if none is saved yet. */
export async function getExportMapping(): Promise<ExportMappingConfig | null> {
  const res = await fetch('/api/export-mapping', { headers: authHeaders() })
  if (!res.ok) return null
  return res.json() as Promise<ExportMappingConfig>
}

/** Validates and persists the full export mapping config. Returns the saved config or null on error. */
export async function saveExportMapping(
  config: ExportMappingConfig,
): Promise<{ ok: boolean; data?: ExportMappingConfig; error?: string }> {
  const res = await fetch('/api/export-mapping', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json', ...authHeaders() },
    body: JSON.stringify(config),
  })
  if (res.ok) {
    const data = (await res.json()) as ExportMappingConfig
    return { ok: true, data }
  }
  const text = await res.text()
  return { ok: false, error: text || `Error ${res.status}` }
}

// ── Export mapping presets ────────────────────────────────────────────────────

/** Returns all saved presets as a name→config map; empty object when none exist. */
export async function getPresets(): Promise<Record<string, ExportMappingConfig>> {
  const res = await fetch('/api/export-mapping/presets', { headers: authHeaders() })
  if (!res.ok) return {}
  return res.json() as Promise<Record<string, ExportMappingConfig>>
}

/** Creates or updates a named preset. */
export async function savePreset(
  name: string,
  config: ExportMappingConfig,
): Promise<{ ok: boolean; error?: string }> {
  const res = await fetch(`/api/export-mapping/presets/${encodeURIComponent(name)}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json', ...authHeaders() },
    body: JSON.stringify(config),
  })
  if (res.ok) return { ok: true }
  const text = await res.text()
  return { ok: false, error: text || `Error ${res.status}` }
}

/** Deletes a named preset. */
export async function deletePreset(name: string): Promise<{ ok: boolean; error?: string }> {
  const res = await fetch(`/api/export-mapping/presets/${encodeURIComponent(name)}`, {
    method: 'DELETE',
    headers: authHeaders(),
  })
  if (res.ok) return { ok: true }
  return { ok: false, error: `Error ${res.status}` }
}
