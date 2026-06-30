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

export async function listErpRecords(): Promise<ErpCiRecord[]> {
  const res = await fetch('/api/erp/records', { headers: authHeaders() })
  if (!res.ok) return []
  return res.json() as Promise<ErpCiRecord[]>
}

export async function getSchema(): Promise<SchemaDefinition | null> {
  const res = await fetch('/api/schema', { headers: authHeaders() })
  if (!res.ok) return null
  return res.json() as Promise<SchemaDefinition>
}

/** Persists the active column set server-side. Returns the validated list actually saved. */
export async function patchSchemaColumns(columns: string[]): Promise<string[]> {
  const res = await fetch('/api/schema/columns', {
    method: 'PATCH',
    headers: authHeaders(),
    body: JSON.stringify({ columns }),
  })
  if (!res.ok) return columns
  return res.json() as Promise<string[]>
}

/** Persists per-column export name overrides. Keys not in the schema are dropped server-side. */
export async function patchSchemaColumnMappings(mappings: Record<string, string>): Promise<Record<string, string>> {
  const res = await fetch('/api/schema/mappings', {
    method: 'PATCH',
    headers: authHeaders(),
    body: JSON.stringify({ mappings }),
  })
  if (!res.ok) return mappings
  return res.json() as Promise<Record<string, string>>
}

// ── Dynamic export mapping ────────────────────────────────────────────────────

export interface MappingField {
  sourceName: string
  targetName: string
  enabled: boolean
}

export interface MappingStrategyOptions {
  sourceField: string
  delimiter: string
}

export interface MappingRelation {
  relatedTable: string
  joinKey: string
  sourceJoinKey: string
  targetField: string
  enabled: boolean
  flattenStrategy: 'string_join' | 'array'
  strategyOptions: MappingStrategyOptions
}

export interface ExportMappingConfig {
  sourceTable: string
  fields: MappingField[]
  relations: MappingRelation[]
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
