<script setup lang="ts">
import { computed } from 'vue'
import type { ImportPlanOperation } from '@/api/importDefinitions'

// Shared by ImportDefinitionPreviewPanel.vue (a sample-file preview) and ImportRunReviewDialog.vue (a
// staged run's four-eyes review) — both need the same "one row's changed fields, grouped by
// CorrelationValue" rendering of a plan's operations, the field-level diff import-definitions.md's
// Slice 6 acceptance criteria calls for. PlanJson is the write-side source of truth (Open Decision
// #11); this component only ever displays operations handed to it, never recomputes anything.
const props = defineProps<{ operations: ImportPlanOperation[] }>()

const groupedByRow = computed(() => {
  const order: string[] = []
  const byRow = new Map<string, ImportPlanOperation[]>()
  for (const op of props.operations) {
    if (!byRow.has(op.correlationValue)) {
      byRow.set(op.correlationValue, [])
      order.push(op.correlationValue)
    }
    byRow.get(op.correlationValue)!.push(op)
  }
  return order.map((correlationValue) => ({ correlationValue, operations: byRow.get(correlationValue)! }))
})
</script>

<template>
  <p v-if="operations.length === 0" class="text-text-secondary text-sm">No field-level changes to apply.</p>
  <div v-else class="overflow-auto max-h-72 border border-border rounded-md">
    <table class="w-full text-xs border-collapse">
      <thead>
        <tr class="text-left bg-surface-elevated">
          <th class="px-2 py-1.5 font-semibold border-b border-border">Row</th>
          <th class="px-2 py-1.5 font-semibold border-b border-border">Column</th>
          <th class="px-2 py-1.5 font-semibold border-b border-border">Old value</th>
          <th class="px-2 py-1.5 font-semibold border-b border-border">New value</th>
        </tr>
      </thead>
      <tbody>
        <template v-for="group in groupedByRow" :key="group.correlationValue">
          <tr v-for="(op, idx) in group.operations" :key="idx" class="border-b border-border last:border-0">
            <td class="px-2 py-1.5 font-mono text-text-secondary whitespace-nowrap">{{ idx === 0 ? group.correlationValue : '' }}</td>
            <td class="px-2 py-1.5 font-mono text-text-primary whitespace-nowrap">{{ op.column }}</td>
            <td class="px-2 py-1.5 font-mono text-text-secondary">{{ op.expectedOldValue ?? '—' }}</td>
            <td class="px-2 py-1.5 font-mono text-text-primary">{{ op.newValue ?? '—' }}</td>
          </tr>
        </template>
      </tbody>
    </table>
  </div>
</template>
