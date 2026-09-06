<script setup lang="ts">
// The read-only "what happened" summary for a Released/Rejected/Failed import run — the ImportRunReviewDialog.vue
// counterpart to its own pending-review Operator/Approver form, pulled out for the same reason as
// ImportRunCountSummary.vue: keeping the dialog's own template from growing another branch.
defineProps<{
  operatedBy: string | null
  approvedBy: string | null
  releasedAt: string | null
  errorMessage: string | null
}>()
</script>

<template>
  <p class="text-text-secondary text-sm">
    <span v-if="operatedBy">Operated by {{ operatedBy }}</span>
    <span v-if="approvedBy"> · approved by {{ approvedBy }}</span>
    <span v-if="releasedAt"> · {{ new Date(releasedAt).toLocaleString() }}</span>
  </p>
  <p v-if="errorMessage" class="text-danger text-sm">{{ errorMessage }}</p>
</template>
