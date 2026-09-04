<script setup lang="ts">
import type { ExportDefinitionRun } from '@/api/exportDefinitions'
import StatusBadge from '@/components/StatusBadge.vue'

// The per-definition analogue of ExportRunsTable.vue — deliberately a separate component, not a
// reuse of that one: ExportDefinitionRunEntity has no SHA-256/staging-file/four-eyes fields (those
// model the legacy CI pipeline this doesn't touch, per export-definitions-2.0.md §10), but it does
// carry ConfigVersion/TriggeredBy/IsTestRun, which ExportRunEntity doesn't.
defineProps<{
  runs: ExportDefinitionRun[]
  loading: boolean
  error: string | null
}>()
defineEmits<{ refresh: [] }>()

function formatDate(iso: string | null | undefined): string {
  if (!iso) return '—'
  return new Date(iso.includes('Z') ? iso : iso + 'Z').toLocaleString()
}
</script>

<template>
  <div>
    <div class="flex items-center gap-3 mb-3">
      <h2 class="m-0 text-base font-semibold text-text-primary">Execution History</h2>
      <button
        class="ml-auto px-2.5 py-1 border border-border-strong rounded-md bg-surface text-xs text-text-secondary cursor-pointer disabled:opacity-50 hover:enabled:bg-surface-elevated"
        :disabled="loading"
        @click="$emit('refresh')"
      >Refresh</button>
    </div>

    <p v-if="loading" class="text-text-secondary text-sm">Loading…</p>
    <p v-else-if="error" class="text-danger text-sm">{{ error }}</p>
    <p v-else-if="runs.length === 0" class="text-text-secondary text-sm">No runs yet.</p>

    <table v-else class="w-full border-collapse text-sm">
      <thead>
        <tr>
          <th class="px-2.5 py-2 text-left bg-surface-elevated font-semibold text-[0.7rem] uppercase tracking-wide border-b border-border">Started</th>
          <th class="px-2.5 py-2 text-left bg-surface-elevated font-semibold text-[0.7rem] uppercase tracking-wide border-b border-border">Status</th>
          <th class="px-2.5 py-2 text-left bg-surface-elevated font-semibold text-[0.7rem] uppercase tracking-wide border-b border-border">Records</th>
          <th class="px-2.5 py-2 text-left bg-surface-elevated font-semibold text-[0.7rem] uppercase tracking-wide border-b border-border">Config v.</th>
          <th class="px-2.5 py-2 text-left bg-surface-elevated font-semibold text-[0.7rem] uppercase tracking-wide border-b border-border">Triggered by</th>
          <th class="px-2.5 py-2 text-left bg-surface-elevated font-semibold text-[0.7rem] uppercase tracking-wide border-b border-border">Error</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="run in runs" :key="run.id" class="hover:bg-surface-elevated">
          <td class="px-2.5 py-1.5 border-b border-border whitespace-nowrap">{{ formatDate(run.startedAt) }}</td>
          <td class="px-2.5 py-1.5 border-b border-border">
            <StatusBadge :status="run.status" />
            <span v-if="run.isTestRun" class="ml-1.5 text-[0.65rem] uppercase tracking-wide text-text-muted">test</span>
          </td>
          <td class="px-2.5 py-1.5 border-b border-border">{{ run.recordCount }}</td>
          <td class="px-2.5 py-1.5 border-b border-border">{{ run.configVersion }}</td>
          <td class="px-2.5 py-1.5 border-b border-border">{{ run.triggeredBy }}</td>
          <td class="px-2.5 py-1.5 border-b border-border text-danger">{{ run.errorMessage ?? '—' }}</td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
