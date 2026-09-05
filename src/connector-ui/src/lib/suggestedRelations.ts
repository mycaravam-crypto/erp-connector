import type { SourceSchema } from '@/api/connection'
import type { SuggestedRelation } from '@/components/SuggestedRelations.vue'

// Forward FKs: a column on `tableName` itself pointing at another table's key (e.g.
// `item.manufacturer_id -> manufacturer.id`) is a 1:1 lookup, suggested as an "object".
function findForwardRelations(schema: SourceSchema, tableName: string): SuggestedRelation[] {
  const columns = schema.tables.find((t) => t.name === tableName)?.columns ?? []
  return columns
    .filter((c) => c.foreignKeyTable && c.foreignKeyTable !== tableName && c.foreignKeyColumn)
    .map((c) => ({ relatedTable: c.foreignKeyTable!, joinKey: c.foreignKeyColumn!, sourceJoinKey: c.name, kind: 'object' as const }))
}

// Reverse FKs: another table's column pointing back at `tableName` (e.g.
// `address.item_id -> item.id`) is a 1:N collection, suggested as an "array".
function findReverseRelations(schema: SourceSchema, tableName: string): SuggestedRelation[] {
  const suggestions: SuggestedRelation[] = []
  for (const t of schema.tables) {
    if (t.name === tableName) continue
    for (const c of t.columns) {
      if (c.foreignKeyTable === tableName && c.foreignKeyColumn) {
        suggestions.push({ relatedTable: t.name, joinKey: c.name, sourceJoinKey: c.foreignKeyColumn, kind: 'array' })
      }
    }
  }
  return suggestions
}

/**
 * FK-based relation suggestions for a given table, in both directions the schema's foreign keys
 * actually express (see {@link findForwardRelations} and {@link findReverseRelations}). Shared by
 * the legacy mapping tree (SchemaView.vue, which only wants the reverse/array shape — see its
 * `.filter(s => s.kind === 'array')`) and the Export Definitions tree builder
 * (export-definitions-2.0.md §7 — "reuse, don't rebuild"), so both editors detect relations from
 * one piece of logic instead of two copies drifting apart.
 */
export function findSuggestedRelations(
  schema: SourceSchema | null,
  tableName: string | null | undefined,
  existing: readonly { relatedTable: string; joinKey: string; sourceJoinKey: string }[],
): SuggestedRelation[] {
  if (!schema || !tableName) return []

  const suggestions = [...findForwardRelations(schema, tableName), ...findReverseRelations(schema, tableName)]

  return suggestions.filter(
    (s) =>
      !existing.some(
        (r) => r.relatedTable === s.relatedTable && r.joinKey === s.joinKey && r.sourceJoinKey === s.sourceJoinKey,
      ),
  )
}
