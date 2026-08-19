import { getToken } from './auth'

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

/** Read-only reference view of the negotiated ICD export contract. */
export async function getSchema(): Promise<SchemaDefinition | null> {
  const res = await fetch('/api/schema', { headers: authHeaders() })
  if (!res.ok) return null
  return res.json() as Promise<SchemaDefinition>
}
