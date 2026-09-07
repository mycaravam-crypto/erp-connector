<script setup lang="ts">
// The Open Decision #11 count breakdown for one import run — pulled out of ImportRunReviewDialog.vue
// purely to keep that file's already-branchy template (loading/error/pending/terminal states) from
// growing further; this piece itself has almost no branching of its own.
import Badge from '@/components/ui/Badge.vue'

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
  <div class="flex flex-wrap gap-2 mb-4">
    <Badge variant="neutral">{{ matchedCount }} matched</Badge>
    <Badge variant="success">{{ changedCount }} changed</Badge>
    <Badge variant="neutral">{{ unchangedCount }} unchanged</Badge>
    <Badge variant="warning">{{ rejectedCount }} rejected</Badge>
    <Badge variant="warning">{{ conflictCount }} conflicted</Badge>
    <Badge variant="danger">{{ invalidCount }} invalid</Badge>
  </div>
  <p v-if="conflictCount > 0" class="text-xs text-warning mt-0 mb-3">
    Conflicted rows were excluded because the ERP value moved since this run was staged — they were
    never overwritten (Open Decision #12).
  </p>
</template>
