import { getToken } from './auth'

export interface ErpCiRecord {
  id: string
  serial: string | null
  status: string | null
  commissionDate: string | null
  articleName: string | null
  partNumber: string | null
  manufacturer: string | null
  maintenancePlanStatus: string | null
  allocationChartRef: string | null
  parentId: string | null
  parentSerial: string | null
  inScope: boolean
  exclusionReason: string | null
  technicianName: string | null
  storageLocation: string | null
}

export interface SchemaColumnDef {
  name: string
  erpSource: string
  type: string
  notes: string
  active: boolean
  exportName: string | null
}

export interface SchemaDefinition {
  version: string
  columns: SchemaColumnDef[]
}

function authHeaders(): Record<string, string> {
  const token = getToken()
  const headers: Record<string, string> = { 'Content-Type': 'application/json' }
  if (token) headers['Authorization'] = `Bearer ${token}`
  return headers
}

export interface ErpRecordsResult {
  records: ErpCiRecord[]
  total: number
}

export async function listErpRecords(limit?: number): Promise<ErpRecordsResult> {
  const url = limit !== undefined ? `/api/erp/records?limit=${limit}` : '/api/erp/records'
  const res = await fetch(url, { headers: authHeaders() })
  if (!res.ok) return { records: [], total: 0 }
  return res.json() as Promise<ErpRecordsResult>
}

export async function getSchema(): Promise<SchemaDefinition | null> {
  const res = await fetch('/api/schema', { headers: authHeaders() })
  if (!res.ok) return null
  return res.json() as Promise<SchemaDefinition>
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
