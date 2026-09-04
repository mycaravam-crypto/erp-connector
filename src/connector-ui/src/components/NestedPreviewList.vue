<script setup lang="ts">
defineProps<{
  records: unknown[]
  totalCount: number
  max: number
}>()
</script>

<template>
  <div v-if="records.length > 0" class="max-h-80 overflow-y-auto border border-border rounded-lg divide-y divide-border">
    <pre
      v-for="(rec, idx) in records"
      :key="idx"
      class="m-0 px-3 py-2 text-xs font-mono text-text-primary whitespace-pre-wrap break-all"
    >{{ JSON.stringify(rec, null, 2) }}</pre>
  </div>
  <p v-if="totalCount > max" class="text-xs text-text-muted mt-1.5">
    Showing first {{ max }} records — preview is capped at 50; the full export includes all in-scope records.
  </p>
  <p v-if="records.length === 0" class="text-text-secondary text-sm">No in-scope records found.</p>
</template>
