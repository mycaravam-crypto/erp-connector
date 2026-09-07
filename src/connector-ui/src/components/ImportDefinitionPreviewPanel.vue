<script setup lang="ts">
// Runs POST /api/import-definitions/{id}/preview — parses + walks + plans a sample inbound file against
// this saved definition, with zero persistence (no ImportRunEntity, nothing written to the ERP). Unlike
// the export side's preview (which just re-runs the live query), the import side has nothing to run
// against without a file: Slice 4's inbound/ folder watcher is the real trigger, so an operator pastes a
// sample ImportEnvelope JSON here to sanity-check the tree before a real vendor file ever arrives.
import type { ImportPlan } from '@/api/importDefinitions'
import ImportPlanDiffTable from '@/components/ImportPlanDiffTable.vue'
import Badge from '@/components/ui/Badge.vue'
import SectionHeader from '@/components/ui/SectionHeader.vue'

const inboundJson = defineModel<string>('inboundJson', { default: '' })

defineProps<{
  plan: ImportPlan | null
  loading: boolean
  error: string | null
}>()
defineEmits<{ refresh: [] }>()
</script>

<template>
  <div>
    <SectionHeader title="Preview" class="mb-1" />
    <p class="text-xs text-text-secondary m-0 mb-2">
      Paste a sample <code>ImportEnvelope</code> JSON (schemaVersion + records[]) to see what this
      definition would do to it — nothing is written, and no run is recorded.
    </p>

    <textarea
      v-model="inboundJson"
      rows="6"
      placeholder='{"schemaVersion": 1, "definition": "...", "records": [...]}'
      aria-label="Sample inbound JSON"
      class="w-full px-3 py-2 border border-border-strong rounded-md text-xs font-mono text-text-primary outline-none focus:border-brand mb-2"
    ></textarea>

    <button
      class="px-2.5 py-1 border border-border-strong rounded-md bg-surface text-xs text-text-secondary cursor-pointer disabled:opacity-50 hover:enabled:bg-surface-elevated mb-3"
      :disabled="loading || inboundJson.trim() === ''"
      @click="$emit('refresh')"
    >{{ loading ? 'Previewing…' : 'Preview' }}</button>

    <p v-if="error" class="text-danger text-sm">{{ error }}</p>

    <template v-else-if="plan">
      <div class="flex flex-wrap gap-2 mb-3">
        <Badge variant="neutral">{{ plan.recordCount }} record(s)</Badge>
        <Badge variant="success">{{ plan.changedCount }} changed</Badge>
        <Badge variant="neutral">{{ plan.unchangedCount }} unchanged</Badge>
        <Badge variant="warning">{{ plan.rejectedCount }} rejected</Badge>
        <Badge variant="danger">{{ plan.invalidCount }} invalid</Badge>
      </div>

      <ImportPlanDiffTable :operations="plan.operations" />
    </template>
  </div>
</template>
