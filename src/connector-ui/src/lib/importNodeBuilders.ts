import type { SourceTable } from '@/api/connection'
import type { ImportMappingSuggestion, ImportNode } from '@/api/importDefinitions'
import { blankFieldMapping } from './exportNodeBuilders'

/** An empty root node — the starting tree for a brand-new import definition. */
export function blankRootNode(): ImportNode {
  return {
    sourceKey: 'root',
    kind: 'root',
    targetColumn: null,
    relatedTable: null,
    joinKey: null,
    sourceJoinKey: null,
    onMissingChild: 'reject',
    mapping: null,
    children: [],
    enabled: true,
  }
}

/** Every enabled scalar-field TargetColumn the tree currently references, excluding the root match
 * field — the backend only ever looks for it among the root's direct children
 * (ImportNodeWalker.FindMatchField) and never treats it as a write target. Feeds
 * ImportAllowedColumnsEditor.vue's "missing from allowlist" hint, mirroring the recursive collection
 * ImportDefinitionEndpoints.ValidateNode does server-side. */
export function collectWritableTargets(root: ImportNode, rootMatchColumn: string): string[] {
  const targets: string[] = []

  function visit(node: ImportNode, isRootChild: boolean) {
    if (!node.enabled) return
    if (node.kind === 'scalar-field' && node.targetColumn) {
      if (!(isRootChild && node.targetColumn === rootMatchColumn)) targets.push(node.targetColumn)
    }
    for (const child of node.children) visit(child, false)
  }

  for (const child of root.children) visit(child, true)
  return [...new Set(targets)]
}

/** One disabled scalar-field node per column of `tableName` — the same "pick columns via checkbox" UX
 * ExportNodeTreeEditor.vue gives, shown when a related table is picked for an object/array node. */
export function columnsAsDisabledScalarFields(
  tableName: string | null | undefined,
  availableTables: SourceTable[],
): ImportNode[] {
  const columns = availableTables.find((t) => t.name === tableName)?.columns ?? []
  return columns.map((c) => ({
    sourceKey: c.name,
    kind: 'scalar-field',
    targetColumn: c.name,
    relatedTable: null,
    joinKey: null,
    sourceJoinKey: null,
    onMissingChild: 'reject',
    mapping: blankFieldMapping(),
    children: [],
    enabled: false,
  }))
}

/**
 * "Create from export" (knowledge/pipeline/import-mapping-presets.md §3.4/§4): builds the root node a New
 * Import Definition starts from once an operator accepts a suggestion. Same starting point as picking the
 * root table by hand (one disabled scalar-field node per column), except the deterministic root match
 * field is pre-enabled and correctly keyed to the export's own JSON field name, and every best-effort
 * candidate field (still unchecked/disabled — the operator must explicitly enable each one) gets its
 * SourceKey pre-filled so accepting it later is a checkbox, not a retype.
 */
export function applyImportMappingSuggestion(
  suggestion: ImportMappingSuggestion,
  availableTables: SourceTable[],
): ImportNode {
  const children = columnsAsDisabledScalarFields(suggestion.rootTable, availableTables)

  const matchNode = children.find((c) => c.targetColumn === suggestion.rootMatchColumn)
  if (matchNode) {
    matchNode.enabled = true
    matchNode.sourceKey = suggestion.rootMatchSourceKey
  }

  for (const candidate of suggestion.candidateFields) {
    const node = children.find((c) => c.targetColumn === candidate.targetColumn)
    if (node) node.sourceKey = candidate.sourceKey
  }

  return { ...blankRootNode(), children }
}
