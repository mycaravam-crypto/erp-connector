<script setup lang="ts">
import { ref } from 'vue'
import { RouterLink } from 'vue-router'
import type { ExportSummary } from '@/api/exports'
import StatusBadge from '@/components/StatusBadge.vue'
import { Check, Copy } from 'lucide-vue-next'
import Icon from '@/components/ui/Icon.vue'
import Button from '@/components/ui/Button.vue'

defineProps<{
  runs: ExportSummary[]
  loading: boolean
  error: string | null
}>()
defineEmits<{ (e: 'refresh'): void }>()

function formatDate(iso: string | null | undefined): string {
  if (!iso) return '—'
  return new Date(iso.includes('Z') ? iso : iso + 'Z').toLocaleString()
}

const copiedSeqNo = ref<number | null>(null)
async function copySha(seqNo: number, hash: string) {
  await navigator.clipboard.writeText(hash)
  copiedSeqNo.value = seqNo
  setTimeout(() => {
    copiedSeqNo.value = null
  }, 1500)
}
</script>

<template>
  <div class="mb-8">
    <div class="flex items-center gap-3 mb-3">
      <h2 class="m-0 text-base font-semibold text-text-primary">Export Runs</h2>
      <Button variant="secondary" class="ml-auto" :loading="loading" @click="$emit('refresh')">Refresh</Button>
    </div>

    <div v-if="!loading && runs.some((r) => r.isStale)" class="bg-warning-bg border border-warning rounded-md px-4 py-2.5 text-sm text-warning mb-3" role="alert">
      <strong>Action required:</strong> one or more export runs have been pending for over 24 hours.
      Please review and release or investigate.
    </div>

    <p v-if="loading" class="text-text-secondary text-sm">Loading…</p>
    <p v-else-if="error" class="text-danger text-sm">{{ error }}</p>

    <div v-else class="rounded-lg border border-border overflow-x-auto">
      <p v-if="runs.length === 0" class="text-text-secondary text-sm text-center py-6">No export runs yet.</p>
      <table v-else class="w-full border-collapse text-sm">
        <thead>
          <tr>
            <th class="px-3 py-2 text-left bg-surface-elevated font-semibold text-[0.7rem] uppercase tracking-wide border-b border-border whitespace-nowrap">#</th>
            <th class="px-3 py-2 text-left bg-surface-elevated font-semibold text-[0.7rem] uppercase tracking-wide border-b border-border whitespace-nowrap">Extracted At</th>
            <th class="px-3 py-2 text-right bg-surface-elevated font-semibold text-[0.7rem] uppercase tracking-wide border-b border-border whitespace-nowrap">Records</th>
            <th class="px-3 py-2 text-left bg-surface-elevated font-semibold text-[0.7rem] uppercase tracking-wide border-b border-border whitespace-nowrap">SHA-256</th>
            <th class="px-3 py-2 text-left bg-surface-elevated font-semibold text-[0.7rem] uppercase tracking-wide border-b border-border whitespace-nowrap">Status</th>
            <th class="px-3 py-2 text-left bg-surface-elevated font-semibold text-[0.7rem] uppercase tracking-wide border-b border-border whitespace-nowrap">File</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="run in runs" :key="run.sequenceNo" class="border-b border-border last:border-0 hover:bg-surface-elevated transition-colors">
            <td class="px-3 py-2 align-middle whitespace-nowrap">
              <RouterLink :to="{ name: 'export-detail', params: { seqNo: run.sequenceNo } }" class="text-brand no-underline hover:underline">{{ run.sequenceNo }}</RouterLink>
            </td>
            <td class="px-3 py-2 align-middle whitespace-nowrap text-text-secondary">{{ formatDate(run.extractedAt) }}</td>
            <td class="px-3 py-2 align-middle whitespace-nowrap text-right tabular-nums">{{ run.recordCount }}</td>
            <td class="px-3 py-2 align-middle whitespace-nowrap">
              <button
                class="group inline-flex items-center gap-1 bg-transparent border-0 p-0 cursor-pointer"
                :title="copiedSeqNo === run.sequenceNo ? 'Copied!' : 'Click to copy full hash'"
                @click="copySha(run.sequenceNo, run.sha256Short)"
              >
                <code class="text-xs text-text-secondary group-hover:text-text-primary">{{ run.sha256Short }}</code>
                <span class="text-text-muted group-hover:text-text-secondary">
                  <Icon :icon="copiedSeqNo === run.sequenceNo ? Check : Copy" :size="16" />
                </span>
              </button>
            </td>
            <td class="px-3 py-2 align-middle whitespace-nowrap">
              <StatusBadge :status="run.status" />
              <span v-if="run.isStale" class="ml-1.5 inline-block bg-warning-bg border border-warning rounded-full px-1.5 py-0.5 text-[0.65rem] font-bold uppercase tracking-wide text-warning align-middle" title="Pending for over 24 hours">overdue</span>
            </td>
            <td class="px-3 py-2 align-middle text-xs text-text-secondary whitespace-nowrap">{{ run.dataFileName }}</td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>
