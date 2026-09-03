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

/** One row of GET /api/export-definitions/{id}/runs — execution history for a definition. */
export interface ExportDefinitionRun {
  id: number
  configVersion: number
  startedAt: string
  finishedAt: string | null
  status: string
  recordCount: number
  errorMessage: string | null
  triggeredBy: string
  isTestRun: boolean
}

/** Response of POST /api/export-definitions/{id}/preview — capped, untracked, no history row written. */
export interface ExportDefinitionPreview {
  recordCount: number
  records: unknown[]
}

type ApiResult<T> = { ok: true; data: T } | { ok: false; error: string }

async function postForResult<T>(url: string): Promise<ApiResult<T>> {
  const res = await fetch(url, { method: 'POST', headers: authHeaders() })
  return toApiResult<T>(res)
}

async function sendJsonForResult<T>(url: string, method: string, body: unknown): Promise<ApiResult<T>> {
  const res = await fetch(url, {
    method,
    headers: { 'Content-Type': 'application/json', ...authHeaders() },
    body: JSON.stringify(body),
  })
  return toApiResult<T>(res)
}

async function toApiResult<T>(res: Response): Promise<ApiResult<T>> {
  if (res.ok) return { ok: true, data: (await res.json()) as T }
  const text = await res.text().catch(() => '')
  return { ok: false, error: text || `Error ${res.status}` }
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
): Promise<ApiResult<ExportDefinition>> {
  return sendJsonForResult<ExportDefinition>(`/api/export-definitions/${id}`, 'PUT', request)
}

/** Runs the definition against the live connection (capped at 50 records) without writing a real export. */
export async function testExportDefinition(id: number): Promise<ApiResult<ExportDefinitionTestResult>> {
  return postForResult<ExportDefinitionTestResult>(`/api/export-definitions/${id}/test`)
}

/** Creates a new export definition. */
export async function createExportDefinition(request: ExportDefinitionRequest): Promise<ApiResult<ExportDefinition>> {
  return sendJsonForResult<ExportDefinition>('/api/export-definitions', 'POST', request)
}

/** Permanently deletes an export definition (and, per the backend, none of its run history — the row is gone). */
export async function deleteExportDefinition(id: number): Promise<boolean> {
  const res = await fetch(`/api/export-definitions/${id}`, { method: 'DELETE', headers: authHeaders() })
  return res.ok
}

/** Copies a definition's tree/format into a new, disabled, manual-only definition. */
export async function duplicateExportDefinition(id: number, name?: string): Promise<ApiResult<ExportDefinition>> {
  return sendJsonForResult<ExportDefinition>(
    `/api/export-definitions/${id}/duplicate`,
    'POST',
    name ? { name } : {},
  )
}

/** Toggles IsEnabled without touching the rest of the definition — the list view's per-row switch. */
export async function setExportDefinitionEnabled(id: number, enabled: boolean): Promise<ApiResult<ExportDefinition>> {
  return sendJsonForResult<ExportDefinition>(`/api/export-definitions/${id}/enable`, 'PATCH', { enabled })
}

/** Capped, untracked query preview — runs the same query path Run Now uses (export-definitions-2.0.md §7). */
export async function previewExportDefinition(id: number): Promise<ApiResult<ExportDefinitionPreview>> {
  return postForResult<ExportDefinitionPreview>(`/api/export-definitions/${id}/preview`)
}

/** Execution history for a definition, most recent first. */
export async function listExportDefinitionRuns(id: number): Promise<ExportDefinitionRun[]> {
  const res = await fetch(`/api/export-definitions/${id}/runs`, { headers: authHeaders() })
  if (!res.ok) return []
  return res.json() as Promise<ExportDefinitionRun[]>
}

/** Triggers a real run and returns the built artifact as a downloadable Blob plus its record count. */
export async function runExportDefinition(
  id: number,
): Promise<{ ok: true; blob: Blob; fileName: string; recordCount: number } | { ok: false; error: string }> {
  const res = await fetch(`/api/export-definitions/${id}/run`, { method: 'POST', headers: authHeaders() })
  if (!res.ok) {
    const text = await res.text().catch(() => '')
    return { ok: false, error: text || `Error ${res.status}` }
  }
  const disposition = res.headers.get('Content-Disposition') ?? ''
  const fileName = /filename="?([^";]+)"?/.exec(disposition)?.[1] ?? `export-${id}`
  const recordCount = Number(res.headers.get('X-Record-Count') ?? '0')
  return { ok: true, blob: await res.blob(), fileName, recordCount }
}
