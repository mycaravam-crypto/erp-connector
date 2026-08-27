import { authHeaders } from './auth'

// Mirrors Connector.Core.DynamicExport.ExportNode / FieldMapping (see ExportNode.cs) — the tree-shaped
// config behind each ExportDefinition. Kind is "root" | "scalar-field" | "object" | "array".
export interface FieldMapping {
  defaultValue: string | null
  transform: string
  transformArg: string | null
  dataType: string
}

export interface ExportNode {
  targetKey: string
  kind: string
  sourceField: string | null
  relatedTable: string | null
  joinKey: string | null
  sourceJoinKey: string | null
  filter: string | null
  mapping: FieldMapping | null
  children: ExportNode[]
  enabled: boolean
}

export interface ExportDefinitionSummary {
  id: number
  name: string
  description: string | null
  rootTable: string
  outputFormat: string
  isEnabled: boolean
  schedule: string | null
  configVersion: number
  createdBy: string
  createdAt: string
  updatedBy: string | null
  updatedAt: string | null
}

export interface ExportDefinition extends ExportDefinitionSummary {
  rootNode: ExportNode
}

export interface ExportDefinitionRequest {
  name: string
  description: string | null
  rootTable: string
  rootNode: ExportNode
  outputFormat: string
  isEnabled: boolean
  schedule: string | null
}

export interface ExportDefinitionTestResult {
  runId: number
  status: string
  recordCount: number
  configVersion: number
  startedAt: string
  finishedAt: string | null
  errorMessage: string | null
  isTestRun: boolean
}

/** Returns all saved export definitions (summary rows, no RootNode). */
export async function listExportDefinitions(): Promise<ExportDefinitionSummary[]> {
  const res = await fetch('/api/export-definitions', { headers: authHeaders() })
  if (!res.ok) return []
  return res.json() as Promise<ExportDefinitionSummary[]>
}

/** Returns one export definition including its full RootNode tree, or null if it doesn't exist. */
export async function getExportDefinition(id: number): Promise<ExportDefinition | null> {
  const res = await fetch(`/api/export-definitions/${id}`, { headers: authHeaders() })
  if (!res.ok) return null
  return res.json() as Promise<ExportDefinition>
}

/** Validates and persists changes to an existing export definition. */
export async function updateExportDefinition(
  id: number,
  request: ExportDefinitionRequest,
): Promise<{ ok: true; data: ExportDefinition } | { ok: false; error: string }> {
  const res = await fetch(`/api/export-definitions/${id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json', ...authHeaders() },
    body: JSON.stringify(request),
  })
  if (res.ok) return { ok: true, data: (await res.json()) as ExportDefinition }
  const text = await res.text().catch(() => '')
  return { ok: false, error: text || `Error ${res.status}` }
}

/** Runs the definition against the live connection (capped at 50 records) without writing a real export. */
export async function testExportDefinition(
  id: number,
): Promise<{ ok: true; data: ExportDefinitionTestResult } | { ok: false; error: string }> {
  const res = await fetch(`/api/export-definitions/${id}/test`, {
    method: 'POST',
    headers: authHeaders(),
  })
  if (res.ok) return { ok: true, data: (await res.json()) as ExportDefinitionTestResult }
  const text = await res.text().catch(() => '')
  return { ok: false, error: text || `Error ${res.status}` }
}
