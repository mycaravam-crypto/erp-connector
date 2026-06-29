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
