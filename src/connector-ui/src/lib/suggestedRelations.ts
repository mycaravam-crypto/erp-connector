import type { SourceSchema } from '@/api/connection'
import type { SuggestedRelation } from '@/components/SuggestedRelations.vue'

/**
 * FK-based relation suggestions for a given table: every other table with a foreign key column
 * pointing back at it. Shared by the legacy mapping tree (SchemaView.vue) and the Export
 * Definitions tree builder (export-definitions-2.0.md §7 — "reuse, don't rebuild") so both editors
 * detect the same relations from one piece of logic instead of two copies drifting apart.
 */
export function findSuggestedRelations(
  schema: SourceSchema | null,
  tableName: string | null | undefined,
  existing: readonly { relatedTable: string; joinKey: string; sourceJoinKey: string }[],
): SuggestedRelation[] {
  if (!schema || !tableName) return []

  const suggestions: SuggestedRelation[] = []
  for (const t of schema.tables) {
    if (t.name === tableName) continue
    for (const c of t.columns) {
      if (c.foreignKeyTable === tableName && c.foreignKeyColumn) {
        suggestions.push({ relatedTable: t.name, joinKey: c.name, sourceJoinKey: c.foreignKeyColumn })
      }
    }
  }

  return suggestions.filter(
    (s) =>
      !existing.some(
        (r) => r.relatedTable === s.relatedTable && r.joinKey === s.joinKey && r.sourceJoinKey === s.sourceJoinKey,
      ),
  )
}
