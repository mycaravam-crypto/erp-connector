import type { SourceTable } from '@/api/connection'
import type { ImportNode } from '@/api/importDefinitions'
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
