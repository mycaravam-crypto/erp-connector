<script setup lang="ts">
// The Open Decision #11 count breakdown for one import run — pulled out of ImportRunReviewDialog.vue
// purely to keep that file's already-branchy template (loading/error/pending/terminal states) from
// growing further; this piece itself has almost no branching of its own.
defineProps<{
  matchedCount: number
  changedCount: number
  unchangedCount: number
  rejectedCount: number
  conflictCount: number
  invalidCount: number
}>()
</script>

<template>
  <div class="flex flex-wrap gap-2 mb-4 text-xs">
    <span class="px-2 py-1 rounded-full bg-surface-elevated text-text-secondary">{{ matchedCount }} matched</span>
    <span class="px-2 py-1 rounded-full bg-success-bg text-success">{{ changedCount }} changed</span>
    <span class="px-2 py-1 rounded-full bg-surface-elevated text-text-muted">{{ unchangedCount }} unchanged</span>
    <span class="px-2 py-1 rounded-full bg-warning-bg text-warning">{{ rejectedCount }} rejected</span>
    <span class="px-2 py-1 rounded-full bg-warning-bg text-warning">{{ conflictCount }} conflicted</span>
    <span class="px-2 py-1 rounded-full bg-danger-bg text-danger">{{ invalidCount }} invalid</span>
  </div>
  <p v-if="conflictCount > 0" class="text-xs text-warning mt-0 mb-3">
    Conflicted rows were excluded because the ERP value moved since this run was staged — they were
    never overwritten (Open Decision #12).
  </p>
</template>
