<script setup lang="ts">
import type { ExportDefinitionRun } from '@/api/exportDefinitions'
import StatusBadge from '@/components/StatusBadge.vue'
import Button from '@/components/ui/Button.vue'

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
      <Button variant="secondary" class="ml-auto" :loading="loading" @click="$emit('refresh')">Refresh</Button>
    </div>

    <p v-if="loading" class="text-text-secondary text-sm">Loading…</p>
    <p v-else-if="error" class="text-danger text-sm">{{ error }}</p>

    <div v-else class="rounded-lg border border-border overflow-x-auto">
      <p v-if="runs.length === 0" class="text-text-secondary text-sm text-center py-6">No runs yet.</p>
      <table v-else class="w-full border-collapse text-sm">
        <thead>
          <tr>
            <th class="px-3 py-2 text-left bg-surface-elevated font-semibold text-[0.7rem] uppercase tracking-wide border-b border-border whitespace-nowrap">Started</th>
            <th class="px-3 py-2 text-left bg-surface-elevated font-semibold text-[0.7rem] uppercase tracking-wide border-b border-border whitespace-nowrap">Status</th>
            <th class="px-3 py-2 text-right bg-surface-elevated font-semibold text-[0.7rem] uppercase tracking-wide border-b border-border whitespace-nowrap">Records</th>
            <th class="px-3 py-2 text-left bg-surface-elevated font-semibold text-[0.7rem] uppercase tracking-wide border-b border-border whitespace-nowrap">Config v.</th>
            <th class="px-3 py-2 text-left bg-surface-elevated font-semibold text-[0.7rem] uppercase tracking-wide border-b border-border whitespace-nowrap">Triggered by</th>
            <th class="px-3 py-2 text-left bg-surface-elevated font-semibold text-[0.7rem] uppercase tracking-wide border-b border-border">Error</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="run in runs" :key="run.id" class="border-b border-border last:border-0 hover:bg-surface-elevated transition-colors">
            <td class="px-3 py-2 whitespace-nowrap text-text-secondary">{{ formatDate(run.startedAt) }}</td>
            <td class="px-3 py-2 whitespace-nowrap">
              <StatusBadge :status="run.status" />
              <span v-if="run.isTestRun" class="ml-1.5 text-[0.65rem] uppercase tracking-wide text-text-muted">test</span>
            </td>
            <td class="px-3 py-2 text-right tabular-nums whitespace-nowrap">{{ run.recordCount }}</td>
            <td class="px-3 py-2 whitespace-nowrap text-text-secondary">{{ run.configVersion }}</td>
            <td class="px-3 py-2 whitespace-nowrap text-text-secondary">{{ run.triggeredBy }}</td>
            <td class="px-3 py-2 text-danger">{{ run.errorMessage ?? '—' }}</td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>
