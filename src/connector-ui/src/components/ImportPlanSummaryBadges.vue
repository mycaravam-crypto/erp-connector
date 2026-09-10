<script setup lang="ts">
// Split out of ImportDefinitionPreviewPanel.vue purely to keep that panel's own template under fallow's
// complexity gate. isStale means the sample textarea has been edited since `plan` was computed (Preview is
// an explicit, manual action — it never reruns just because the text changed), so the count badges below
// dim and a banner explains why they may no longer match what's on screen.
import type { ImportPlan } from '@/api/importDefinitions'
import Badge from '@/components/ui/Badge.vue'

defineProps<{ plan: ImportPlan; isStale: boolean }>()
</script>

<template>
  <div v-if="isStale" class="flex items-center gap-1.5 mb-2 text-xs text-warning">
    <span aria-hidden="true">⚠</span>
    <span>Sample changed since this result — click Preview to refresh it.</span>
  </div>
  <div class="flex flex-wrap gap-2 mb-3 transition-opacity" :class="isStale ? 'opacity-50' : ''">
    <Badge variant="neutral">{{ plan.recordCount }} record(s)</Badge>
    <Badge variant="success" title="Fields whose sample value differs from the current database value">{{ plan.changedCount }} changed</Badge>
    <Badge variant="neutral" title="Matched rows where every mapped field already equals the database">{{ plan.unchangedCount }} unchanged</Badge>
    <Badge variant="warning" title="Correlation key matched no row in the database">{{ plan.rejectedCount }} rejected</Badge>
    <Badge variant="danger" title="Record itself was malformed (not a JSON object, or no correlation value)">{{ plan.invalidCount }} invalid</Badge>
  </div>
</template>
