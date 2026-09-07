<script setup lang="ts">
// The Open Decision #11 count breakdown for one import run — pulled out of ImportRunReviewDialog.vue
// purely to keep that file's already-branchy template (loading/error/pending/terminal states) from
// growing further; this piece itself has almost no branching of its own.
import Badge from '@/components/ui/Badge.vue'
import HelpTooltip from '@/components/ui/HelpTooltip.vue'

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
  <div class="flex flex-wrap items-center gap-2 mb-4">
    <HelpTooltip label="What do these counts mean?" title="Reading the count breakdown">
      <ul>
        <li><strong>Matched</strong> — the inbound record found a row in the ERP by its correlation key.</li>
        <li><strong>Changed</strong> — a matched row where at least one allowed column's value actually differs; these are what get written on release.</li>
        <li><strong>Unchanged</strong> — matched, but the inbound value already equals what's in the ERP — nothing to write.</li>
        <li><strong>Rejected</strong> — no matching row was found (see this definition's "If unmatched" setting).</li>
        <li><strong>Conflicted</strong> — the ERP value changed after this run was staged, so it was left alone rather than overwritten with stale data.</li>
        <li><strong>Invalid</strong> — the record itself was malformed (missing/bad correlation value) and couldn't be evaluated at all.</li>
      </ul>
    </HelpTooltip>
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
