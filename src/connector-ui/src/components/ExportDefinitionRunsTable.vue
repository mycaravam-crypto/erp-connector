<script setup lang="ts">
import type { ExportDefinitionRun } from '@/api/exportDefinitions'
import StatusBadge from '@/components/StatusBadge.vue'
import RunHistoryFrame from '@/components/RunHistoryFrame.vue'
import { formatDate } from '@/lib/dates'

// The per-definition analogue of ExportRunsTable.vue — deliberately a separate component, not a
// reuse of that one: ExportDefinitionRunEntity has no sequence/four-eyes fields (those model the
// legacy CI pipeline), but it does carry ConfigVersion/TriggeredBy/IsTestRun, which ExportRunEntity
// doesn't. Only scheduled runs have a staged file (hover shows its SHA-256).
defineProps<{
  runs: ExportDefinitionRun[]
  loading: boolean
  error: string | null
}>()
const emit = defineEmits<{ refresh: [] }>()

const columns = [
  { label: 'Started' },
  { label: 'Status' },
  { label: 'Records', numeric: true },
  { label: 'Config v.' },
  { label: 'Triggered by' },
  { label: 'Staged file' },
  { label: 'Error' },
]
</script>

<template>
  <RunHistoryFrame
    title="Execution History"
    :columns="columns"
    :loading="loading"
    :error="error"
    :empty="runs.length === 0"
    empty-text="No runs yet."
    @refresh="emit('refresh')"
  >
    <tr v-for="run in runs" :key="run.id" class="border-b border-border last:border-0 hover:bg-surface-elevated transition-colors">
      <td class="px-3 py-2 whitespace-nowrap text-text-secondary">{{ formatDate(run.startedAt) }}</td>
      <td class="px-3 py-2 whitespace-nowrap">
        <StatusBadge :status="run.status" />
        <span v-if="run.isTestRun" class="ml-1.5 text-[0.65rem] uppercase tracking-wide text-text-muted">test</span>
      </td>
      <td class="px-3 py-2 text-right tabular-nums whitespace-nowrap">{{ run.recordCount }}</td>
      <td class="px-3 py-2 whitespace-nowrap text-text-secondary">{{ run.configVersion }}</td>
      <td class="px-3 py-2 whitespace-nowrap text-text-secondary">{{ run.triggeredBy }}</td>
      <td class="px-3 py-2 whitespace-nowrap font-mono text-xs text-text-secondary" :title="run.sha256 ?? undefined">{{ run.dataFileName ?? '—' }}</td>
      <td class="px-3 py-2 text-danger">{{ run.errorMessage ?? '—' }}</td>
    </tr>
  </RunHistoryFrame>
</template>
