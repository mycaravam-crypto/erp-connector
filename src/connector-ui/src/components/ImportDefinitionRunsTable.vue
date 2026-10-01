<script setup lang="ts">
import type { ImportDefinitionRun } from '@/api/importDefinitions'
import StatusBadge from '@/components/StatusBadge.vue'
import RunHistoryFrame from '@/components/RunHistoryFrame.vue'
import { formatDate } from '@/lib/dates'

// The import-side analogue of ExportDefinitionRunsTable.vue: same per-definition run-history shape, but
// carries the full count breakdown (changed/unchanged/inserted/rejected/conflicted/
// invalid) instead of a bare record count, and a Review action on PendingReview rows — the entry point
// into ImportRunReviewDialog.vue's four-eyes commit flow.
defineProps<{
  runs: ImportDefinitionRun[]
  loading: boolean
  error: string | null
}>()
const emit = defineEmits<{ refresh: []; review: [runId: number] }>()

const columns = [
  { label: 'Started' },
  { label: 'Status' },
  { label: 'Changed', numeric: true },
  { label: 'Unchanged', numeric: true },
  { label: 'Inserted', numeric: true },
  { label: 'Rejected', numeric: true },
  { label: 'Conflicted', numeric: true },
  { label: 'Invalid', numeric: true },
  { label: 'Triggered by' },
  { label: '' },
]
</script>

<template>
  <RunHistoryFrame
    title="Run History"
    :columns="columns"
    :loading="loading"
    :error="error"
    :empty="runs.length === 0"
    empty-text="No runs yet — the inbound folder watcher stages one here as soon as a matching file arrives."
    @refresh="emit('refresh')"
  >
    <tr v-for="run in runs" :key="run.id" class="border-b border-border last:border-0 hover:bg-surface-elevated transition-colors">
      <td class="px-3 py-2 whitespace-nowrap text-text-secondary">{{ formatDate(run.startedAt) }}</td>
      <td class="px-3 py-2 whitespace-nowrap"><StatusBadge :status="run.status" /></td>
      <td class="px-3 py-2 text-right tabular-nums whitespace-nowrap">{{ run.changedCount }}</td>
      <td class="px-3 py-2 text-right tabular-nums whitespace-nowrap">{{ run.unchangedCount }}</td>
      <td class="px-3 py-2 text-right tabular-nums whitespace-nowrap">{{ run.insertCount }}</td>
      <td class="px-3 py-2 text-right tabular-nums whitespace-nowrap">{{ run.rejectedCount }}</td>
      <td class="px-3 py-2 text-right tabular-nums whitespace-nowrap">{{ run.conflictCount }}</td>
      <td class="px-3 py-2 text-right tabular-nums whitespace-nowrap">{{ run.invalidCount }}</td>
      <td class="px-3 py-2 whitespace-nowrap text-text-secondary">{{ run.triggeredBy }}</td>
      <td class="px-3 py-2 text-right whitespace-nowrap">
        <button
          v-if="run.status === 'PendingReview'"
          type="button"
          class="text-brand text-sm bg-transparent border-0 p-0 cursor-pointer hover:underline"
          @click="emit('review', run.id)"
        >Review</button>
        <button
          v-else
          type="button"
          class="text-text-secondary text-sm bg-transparent border-0 p-0 cursor-pointer hover:underline"
          @click="emit('review', run.id)"
        >Details</button>
      </td>
    </tr>
  </RunHistoryFrame>
</template>
