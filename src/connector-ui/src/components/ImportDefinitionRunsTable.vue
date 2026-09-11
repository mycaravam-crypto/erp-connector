<script setup lang="ts">
import type { ImportDefinitionRun } from '@/api/importDefinitions'
import StatusBadge from '@/components/StatusBadge.vue'
import Button from '@/components/ui/Button.vue'
import { formatDate } from '@/lib/dates'

// The import-side analogue of ExportDefinitionRunsTable.vue: same per-definition run-history shape, but
// carries the full Open Decision #11 count breakdown (matched/changed/unchanged/rejected/conflicted/
// invalid) instead of a bare record count, and a Review action on PendingReview rows — the entry point
// into ImportRunReviewDialog.vue's four-eyes commit flow.
defineProps<{
  runs: ImportDefinitionRun[]
  loading: boolean
  error: string | null
}>()
const emit = defineEmits<{ refresh: []; review: [runId: number] }>()
</script>

<template>
  <div>
    <div class="flex items-center gap-3 mb-3">
      <h2 class="m-0 text-base font-semibold text-text-primary">Run History</h2>
      <Button variant="secondary" class="ml-auto" :loading="loading" @click="$emit('refresh')">Refresh</Button>
    </div>

    <p v-if="loading" class="text-text-secondary text-sm">Loading…</p>
    <p v-else-if="error" class="text-danger text-sm">{{ error }}</p>

    <div v-else class="rounded-lg border border-border overflow-x-auto">
      <p v-if="runs.length === 0" class="text-text-secondary text-sm text-center py-6">No runs yet — the inbound folder watcher stages one here as soon as a matching file arrives.</p>
      <table v-else class="w-full border-collapse text-sm">
        <thead>
          <tr>
            <th class="px-3 py-2 text-left bg-surface-elevated font-semibold text-[0.7rem] uppercase tracking-wide border-b border-border whitespace-nowrap">Started</th>
            <th class="px-3 py-2 text-left bg-surface-elevated font-semibold text-[0.7rem] uppercase tracking-wide border-b border-border whitespace-nowrap">Status</th>
            <th class="px-3 py-2 text-right bg-surface-elevated font-semibold text-[0.7rem] uppercase tracking-wide border-b border-border whitespace-nowrap">Changed</th>
            <th class="px-3 py-2 text-right bg-surface-elevated font-semibold text-[0.7rem] uppercase tracking-wide border-b border-border whitespace-nowrap">Unchanged</th>
            <th class="px-3 py-2 text-right bg-surface-elevated font-semibold text-[0.7rem] uppercase tracking-wide border-b border-border whitespace-nowrap">Rejected</th>
            <th class="px-3 py-2 text-right bg-surface-elevated font-semibold text-[0.7rem] uppercase tracking-wide border-b border-border whitespace-nowrap">Conflicted</th>
            <th class="px-3 py-2 text-right bg-surface-elevated font-semibold text-[0.7rem] uppercase tracking-wide border-b border-border whitespace-nowrap">Invalid</th>
            <th class="px-3 py-2 text-left bg-surface-elevated font-semibold text-[0.7rem] uppercase tracking-wide border-b border-border whitespace-nowrap">Triggered by</th>
            <th class="px-3 py-2 text-left bg-surface-elevated font-semibold text-[0.7rem] uppercase tracking-wide border-b border-border"></th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="run in runs" :key="run.id" class="border-b border-border last:border-0 hover:bg-surface-elevated transition-colors">
            <td class="px-3 py-2 whitespace-nowrap text-text-secondary">{{ formatDate(run.startedAt) }}</td>
            <td class="px-3 py-2 whitespace-nowrap"><StatusBadge :status="run.status" /></td>
            <td class="px-3 py-2 text-right tabular-nums whitespace-nowrap">{{ run.changedCount }}</td>
            <td class="px-3 py-2 text-right tabular-nums whitespace-nowrap">{{ run.unchangedCount }}</td>
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
        </tbody>
      </table>
    </div>
  </div>
</template>
