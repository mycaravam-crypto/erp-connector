<script setup lang="ts">
import { ref } from 'vue'
import type { ExportDetail } from '@/api/exports'

defineProps<{ run: ExportDetail }>()

function formatDate(iso: string | null | undefined): string {
  if (!iso) return '—'
  return new Date(iso.includes('Z') ? iso : iso + 'Z').toLocaleString()
}

const shacopied = ref(false)
async function copySha(hash: string) {
  await navigator.clipboard.writeText(hash)
  shacopied.value = true
  setTimeout(() => {
    shacopied.value = false
  }, 1500)
}
</script>

<template>
  <table class="border-collapse text-sm max-w-xl">
    <tbody>
      <tr>
        <th class="px-3 py-2 text-left font-semibold text-slate-500 w-40 whitespace-nowrap border-b border-slate-200 align-top">Sequence No</th>
        <td class="px-3 py-2 border-b border-slate-200 align-top">{{ run.sequenceNo }}</td>
      </tr>
      <tr>
        <th class="px-3 py-2 text-left font-semibold text-slate-500 w-40 whitespace-nowrap border-b border-slate-200 align-top">Extracted At</th>
        <td class="px-3 py-2 border-b border-slate-200 align-top">{{ formatDate(run.extractedAt) }}</td>
      </tr>
      <tr>
        <th class="px-3 py-2 text-left font-semibold text-slate-500 w-40 whitespace-nowrap border-b border-slate-200 align-top">Record Count</th>
        <td class="px-3 py-2 border-b border-slate-200 align-top">{{ run.recordCount }}</td>
      </tr>
      <tr>
        <th class="px-3 py-2 text-left font-semibold text-slate-500 w-40 whitespace-nowrap border-b border-slate-200 align-top">SHA-256</th>
        <td class="px-3 py-2 border-b border-slate-200 align-top">
          <button
            class="group inline-flex items-center gap-1.5 bg-transparent border-0 p-0 cursor-pointer"
            :title="shacopied ? 'Copied!' : 'Click to copy'"
            @click="copySha(run.sha256)"
          >
            <code class="text-xs break-all text-slate-700 group-hover:text-slate-900">{{ run.sha256 }}</code>
            <span class="text-xs text-slate-400 group-hover:text-slate-600 shrink-0">{{ shacopied ? '✓' : '⎘' }}</span>
          </button>
        </td>
      </tr>
      <tr>
        <th class="px-3 py-2 text-left font-semibold text-slate-500 w-40 whitespace-nowrap border-b border-slate-200 align-top">File</th>
        <td class="px-3 py-2 border-b border-slate-200 align-top">{{ run.dataFileName }}</td>
      </tr>
      <tr v-if="run.releasedAt">
        <th class="px-3 py-2 text-left font-semibold text-slate-500 w-40 whitespace-nowrap border-b border-slate-200 align-top">Released At</th>
        <td class="px-3 py-2 border-b border-slate-200 align-top">{{ formatDate(run.releasedAt) }}</td>
      </tr>
      <tr v-if="run.operatedBy">
        <th class="px-3 py-2 text-left font-semibold text-slate-500 w-40 whitespace-nowrap border-b border-slate-200 align-top">Operated By</th>
        <td class="px-3 py-2 border-b border-slate-200 align-top">{{ run.operatedBy }}</td>
      </tr>
      <tr v-if="run.approvedBy">
        <th class="px-3 py-2 text-left font-semibold text-slate-500 w-40 whitespace-nowrap border-b border-slate-200 align-top">Approved By</th>
        <td class="px-3 py-2 border-b border-slate-200 align-top">{{ run.approvedBy }}</td>
      </tr>
      <tr v-if="run.deliveredAt">
        <th class="px-3 py-2 text-left font-semibold text-slate-500 w-40 whitespace-nowrap border-b border-slate-200 align-top">Delivered At</th>
        <td class="px-3 py-2 border-b border-slate-200 align-top">{{ formatDate(run.deliveredAt) }}</td>
      </tr>
      <tr v-if="run.deliveredBy">
        <th class="px-3 py-2 text-left font-semibold text-slate-500 w-40 whitespace-nowrap border-b border-slate-200 align-top">Delivered By</th>
        <td class="px-3 py-2 border-b border-slate-200 align-top">{{ run.deliveredBy }}</td>
      </tr>
      <tr v-if="run.importedRecordCount !== null">
        <th class="px-3 py-2 text-left font-semibold text-slate-500 w-40 whitespace-nowrap border-b border-slate-200 align-top">Imported Records</th>
        <td class="px-3 py-2 border-b border-slate-200 align-top">{{ run.importedRecordCount }}</td>
      </tr>
      <tr v-if="run.deliveryNotes">
        <th class="px-3 py-2 text-left font-semibold text-slate-500 w-40 whitespace-nowrap border-b border-slate-200 align-top">Delivery Notes</th>
        <td class="px-3 py-2 border-b border-slate-200 align-top">{{ run.deliveryNotes }}</td>
      </tr>
    </tbody>
  </table>
</template>
