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
