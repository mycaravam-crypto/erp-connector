import type { SourceTable } from '@/api/connection'
import type { ExportNode, FieldMapping } from '@/api/exportDefinitions'

/** A neutral starting point for a new scalar-field node's transform config — every FieldMapping
 * member at its backend default (Connector.Core.DynamicExport.FieldTransform.None /
 * FieldDataType.String). */
export function blankFieldMapping(): FieldMapping {
  return { defaultValue: null, transform: 'none', transformArg: null, dataType: 'string' }
}

/** One disabled scalar-field node per column of `tableName` — the "pick columns via checkbox" UX
 * shown when a table is selected for the export root or a related entity, shared by
 * ExportDefinitionEditView.vue (root table) and ExportNodeTreeEditor.vue (nested related tables)
 * so both populate exactly the same way. */
export function columnsAsDisabledScalarFields(
  tableName: string | null | undefined,
  availableTables: SourceTable[],
): ExportNode[] {
  const columns = availableTables.find((t) => t.name === tableName)?.columns ?? []
  return columns.map((c) => ({
    targetKey: c.name,
    kind: 'scalar-field',
    sourceField: c.name,
    relatedTable: null,
    joinKey: null,
    sourceJoinKey: null,
    filter: null,
    mapping: blankFieldMapping(),
    children: [],
    enabled: false,
  }))
}

/** An empty root node — the starting tree for a brand-new export definition. */
export function blankRootNode(): ExportNode {
  return {
    targetKey: 'root',
    kind: 'root',
    sourceField: null,
    relatedTable: null,
    joinKey: null,
    sourceJoinKey: null,
    filter: null,
    mapping: null,
    children: [],
    enabled: true,
  }
}
