import type { ImportNode } from '@/api/importDefinitions'

/** Best-effort, client-side-only mirror of what ImportNodeWalker.DiffScalarFields actually looks at: for
 * each record in a pasted ImportEnvelope, only a property whose key equals some enabled scalar-field
 * child's SourceKey is ever read — any other key on the record is invisible to the walker, no matter what
 * value it carries. Used solely to warn an operator in ImportDefinitionPreviewPanel.vue that editing such a
 * key can never move the "changed" count; never sent to the server and never affects what Preview runs.
 *
 * Returns [] for anything short of a parseable object with a `records` array — malformed/unrelated input
 * still surfaces through the normal Preview error path, this hint must never guess at it. */
export function findUnmappedRootFields(inboundJson: string, rootNode: ImportNode | null): string[] {
  if (!rootNode) return []

  let parsed: unknown
  try {
    parsed = JSON.parse(inboundJson)
  } catch {
    return []
  }
  if (typeof parsed !== 'object' || parsed === null) return []

  const records = (parsed as Record<string, unknown>).records
  if (!Array.isArray(records)) return []

  const mappedKeys = new Set(
    rootNode.children.filter((c) => c.enabled && c.kind === 'scalar-field').map((c) => c.sourceKey),
  )

  const unmapped = new Set<string>()
  for (const record of records) {
    if (typeof record !== 'object' || record === null || Array.isArray(record)) continue
    for (const key of Object.keys(record as Record<string, unknown>)) {
      if (!mappedKeys.has(key)) unmapped.add(key)
    }
  }
  return [...unmapped].sort()
}
