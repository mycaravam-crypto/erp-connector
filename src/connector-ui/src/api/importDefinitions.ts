import { authHeaders } from './auth'
import type { FieldMapping } from './exportDefinitions'

// Mirrors Connector.Core.DynamicImport.ImportNode (see ImportNode.cs) — the write-side counterpart of
// ExportNode. Kind is "root" | "scalar-field" | "object" | "array"; OnMissingChild is "insert" | "reject"
// (the Slice 5 validator rejects "insert" outright for v1, per Open Decision #15).
export interface ImportNode {
  sourceKey: string
  kind: string
  targetColumn: string | null
  relatedTable: string | null
  joinKey: string | null
  sourceJoinKey: string | null
  onMissingChild: string
  mapping: FieldMapping | null
  children: ImportNode[]
  enabled: boolean
}

export interface ImportDefinitionSummary {
  id: number
  name: string
  description: string | null
  rootTable: string
  unmatchedRootPolicy: string
  isEnabled: boolean
  configVersion: number
  createdBy: string
  createdAt: string
  updatedBy: string | null
  updatedAt: string | null
}

export interface ImportDefinition extends ImportDefinitionSummary {
  rootMatchColumn: string
  rootNode: ImportNode
  allowedWritableColumns: string[]
}

export interface ImportDefinitionRequest {
  name: string
  description: string | null
  rootTable: string
  rootMatchColumn: string
  rootNode: ImportNode
  allowedWritableColumns: string[]
  unmatchedRootPolicy: string
  isEnabled: boolean
}

/** One Connector.Core.DynamicImport.ImportPlanOperation — a single column-level write the commit step
 * will make, for a matched/changed row. */
export interface ImportPlanOperation {
  correlationValue: string
  table: string
  keyColumn: string
  keyValue: string
  column: string
  expectedOldValue: string | null
  newValue: string | null
}

/** Response of POST /api/import-definitions/{id}/preview — the computed ImportPlan (Open Decision #11):
 * counts plus the structured operation list for matched/changed rows, no ImportRunEntity written. */
export interface ImportPlan {
  recordCount: number
  matchedCount: number
  changedCount: number
  unchangedCount: number
  rejectedCount: number
  invalidCount: number
  operations: ImportPlanOperation[]
}

/** One row of GET /api/import-definitions/{id}/runs — execution history for a definition. */
export interface ImportDefinitionRun {
  id: number
  configVersion: number
  startedAt: string
  finishedAt: string | null
  status: string
  recordCount: number
  matchedCount: number
  changedCount: number
  unchangedCount: number
  rejectedCount: number
  conflictCount: number
  invalidCount: number
  errorMessage: string | null
  triggeredBy: string
  operatedBy: string | null
  approvedBy: string | null
  releasedAt: string | null
}

/** Response of GET /api/import-runs/{id} — everything the review/diff view needs before an Approver
 * commits: the full count breakdown plus the persisted plan operations. */
export interface ImportRunDetail {
  id: number
  importDefinitionId: number
  importDefinitionName: string
  configVersion: number
  sourceFileName: string
  startedAt: string
  finishedAt: string | null
  status: string
  recordCount: number
  matchedCount: number
  changedCount: number
  unchangedCount: number
  rejectedCount: number
  conflictCount: number
  invalidCount: number
  errorMessage: string | null
  triggeredBy: string
  operatedBy: string | null
  approvedBy: string | null
  releasedAt: string | null
  operations: ImportPlanOperation[]
}

/** Response of the four-eyes release/reject endpoints — the post-action state of one run. */
export interface ImportRun {
  id: number
  importDefinitionId: number
  status: string
  recordCount: number
  matchedCount: number
  changedCount: number
  unchangedCount: number
  rejectedCount: number
  conflictCount: number
  invalidCount: number
  operatedBy: string | null
  approvedBy: string | null
  releasedAt: string | null
  errorMessage: string | null
}

type ApiResult<T> = { ok: true; data: T } | { ok: false; error: string }

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

/** Returns all saved import definitions (summary rows, no RootNode/AllowedWritableColumns). */
export async function listImportDefinitions(): Promise<ImportDefinitionSummary[]> {
  const res = await fetch('/api/import-definitions', { headers: authHeaders() })
  if (!res.ok) return []
  return res.json() as Promise<ImportDefinitionSummary[]>
}

/** Returns one import definition including its full RootNode tree, or null if it doesn't exist. */
export async function getImportDefinition(id: number): Promise<ImportDefinition | null> {
  const res = await fetch(`/api/import-definitions/${id}`, { headers: authHeaders() })
  if (!res.ok) return null
  return res.json() as Promise<ImportDefinition>
}

/** Creates a new import definition. */
export async function createImportDefinition(request: ImportDefinitionRequest): Promise<ApiResult<ImportDefinition>> {
  return sendJsonForResult<ImportDefinition>('/api/import-definitions', 'POST', request)
}

/** Validates and persists changes to an existing import definition. */
export async function updateImportDefinition(
  id: number,
  request: ImportDefinitionRequest,
): Promise<ApiResult<ImportDefinition>> {
  return sendJsonForResult<ImportDefinition>(`/api/import-definitions/${id}`, 'PUT', request)
}

/** Permanently deletes an import definition. */
export async function deleteImportDefinition(id: number): Promise<boolean> {
  const res = await fetch(`/api/import-definitions/${id}`, { method: 'DELETE', headers: authHeaders() })
  return res.ok
}

/** Copies a definition's tree/allowlist into a new, disabled definition. */
export async function duplicateImportDefinition(id: number, name?: string): Promise<ApiResult<ImportDefinition>> {
  return sendJsonForResult<ImportDefinition>(
    `/api/import-definitions/${id}/duplicate`,
    'POST',
    name ? { name } : {},
  )
}

/** Toggles IsEnabled without touching the rest of the definition — the list view's per-row switch. */
export async function setImportDefinitionEnabled(id: number, enabled: boolean): Promise<ApiResult<ImportDefinition>> {
  return sendJsonForResult<ImportDefinition>(`/api/import-definitions/${id}/enable`, 'PATCH', { enabled })
}

/** Parses + walks + plans a sample inbound file against a saved definition — no ImportRunEntity is
 * created and nothing is written to the ERP (Slice 4's folder watcher is the real trigger). */
export async function previewImportDefinition(id: number, inboundJson: string): Promise<ApiResult<ImportPlan>> {
  return sendJsonForResult<ImportPlan>(`/api/import-definitions/${id}/preview`, 'POST', { inboundJson })
}

/** Execution history for a definition, most recent first. */
export async function listImportDefinitionRuns(id: number): Promise<ImportDefinitionRun[]> {
  const res = await fetch(`/api/import-definitions/${id}/runs`, { headers: authHeaders() })
  if (!res.ok) return []
  return res.json() as Promise<ImportDefinitionRun[]>
}

/** Full detail for one run — counts plus the plan operations — for the review/diff dialog. */
export async function getImportRun(id: number): Promise<ImportRunDetail | null> {
  const res = await fetch(`/api/import-runs/${id}`, { headers: authHeaders() })
  if (!res.ok) return null
  return res.json() as Promise<ImportRunDetail>
}

/** Four-eyes commit: applies every matched/changed row still valid at commit time (Open Decision #12). */
export async function releaseImportRun(id: number, approver: string): Promise<ApiResult<ImportRun>> {
  return sendJsonForResult<ImportRun>(`/api/import-runs/${id}/release`, 'POST', { approver })
}

/** Declines a pending run without writing anything to the ERP — single-person, no Approver needed. */
export async function rejectImportRun(id: number): Promise<ApiResult<ImportRun>> {
  const res = await fetch(`/api/import-runs/${id}/reject`, { method: 'POST', headers: authHeaders() })
  return toApiResult<ImportRun>(res)
}
