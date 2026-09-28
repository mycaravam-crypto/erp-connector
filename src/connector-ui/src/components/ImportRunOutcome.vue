<script setup lang="ts">
import { formatDate } from '@/lib/dates'

// The read-only "what happened" summary for a Released/Rejected/Failed import run — the ImportRunReviewDialog.vue
// counterpart to its own pending-review Operator/Approver form, pulled out for the same reason as
// ImportRunCountSummary.vue: keeping the dialog's own template from growing another branch.
defineProps<{
  /** User who released or rejected the run, or null. */
  operatedBy: string | null
  /** Second user who approved a release, or null (rejections need no approver). */
  approvedBy: string | null
  /** When the run was released (ISO 8601), or null. */
  releasedAt: string | null
  /** Why the release failed, or null. */
  errorMessage: string | null
}>()
</script>

<template>
  <p class="text-text-secondary text-sm">
    <span v-if="operatedBy">Operated by {{ operatedBy }}</span>
    <span v-if="approvedBy"> · approved by {{ approvedBy }}</span>
    <span v-if="releasedAt"> · {{ formatDate(releasedAt) }}</span>
  </p>
  <p v-if="errorMessage" class="text-danger text-sm">{{ errorMessage }}</p>
</template>
