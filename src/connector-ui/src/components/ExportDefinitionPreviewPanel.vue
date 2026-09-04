<script setup lang="ts">
// Runs POST /api/export-definitions/{id}/preview — the *same* query path Run Now uses
// (export-definitions-2.0.md §7), just capped and untracked. Records are arbitrary nested JSON
// (every ExportNode shape, regardless of the definition's own OutputFormat, since the format writer
// only applies at Run Now/Test time) — rendered as pretty-printed JSON rather than a flattened table,
// since a 3-level nested tree has no single flat column set to render as a grid.
defineProps<{
  recordCount: number | null
  records: unknown[]
  loading: boolean
  error: string | null
}>()
defineEmits<{ refresh: [] }>()
</script>

<template>
  <div>
    <div class="flex items-center gap-3 mb-1">
      <h2 class="m-0 text-base font-semibold text-text-primary">Preview</h2>
      <span v-if="recordCount !== null" class="text-xs text-text-secondary">{{ recordCount }} record(s) (capped)</span>
      <button
        class="ml-auto px-2.5 py-1 border border-border-strong rounded-md bg-surface text-xs text-text-secondary cursor-pointer disabled:opacity-50 hover:enabled:bg-surface-elevated"
        :disabled="loading"
        @click="$emit('refresh')"
      >{{ loading ? 'Loading…' : 'Preview' }}</button>
    </div>
    <p class="text-xs text-text-secondary m-0 mb-3">Runs the same query Run Now uses, capped, without writing a run to history.</p>

    <p v-if="error" class="text-danger text-sm">{{ error }}</p>
    <p v-else-if="recordCount === 0" class="text-text-secondary text-sm">No records matched.</p>
    <pre
      v-else-if="records.length > 0"
      class="bg-surface-elevated border border-border rounded-md px-3 py-2 text-xs text-text-primary overflow-auto max-h-96"
    >{{ JSON.stringify(records, null, 2) }}</pre>
  </div>
</template>
