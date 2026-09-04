<script setup lang="ts">
import { ref } from 'vue'
import type { ExportDetail } from '@/api/exports'
import { Check, Copy } from 'lucide-vue-next'
import Icon from '@/components/ui/Icon.vue'

defineProps<{ run: ExportDetail }>()

function formatDate(iso: string | null | undefined): string {
  if (!iso) return '—'
  const hasTimezone = /(Z|[+-]\d{2}:\d{2})$/.test(iso)
  return new Date(hasTimezone ? iso : iso + 'Z').toLocaleString()
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
        <th class="px-3 py-2 text-left font-semibold text-text-secondary w-40 whitespace-nowrap border-b border-border align-top">Sequence No</th>
        <td class="px-3 py-2 border-b border-border align-top">{{ run.sequenceNo }}</td>
      </tr>
      <tr>
        <th class="px-3 py-2 text-left font-semibold text-text-secondary w-40 whitespace-nowrap border-b border-border align-top">Extracted At</th>
        <td class="px-3 py-2 border-b border-border align-top">{{ formatDate(run.extractedAt) }}</td>
      </tr>
      <tr>
        <th class="px-3 py-2 text-left font-semibold text-text-secondary w-40 whitespace-nowrap border-b border-border align-top">Record Count</th>
        <td class="px-3 py-2 border-b border-border align-top">{{ run.recordCount }}</td>
      </tr>
      <tr>
        <th class="px-3 py-2 text-left font-semibold text-text-secondary w-40 whitespace-nowrap border-b border-border align-top">SHA-256</th>
        <td class="px-3 py-2 border-b border-border align-top">
          <button
            class="group inline-flex items-center gap-1.5 bg-transparent border-0 p-0 cursor-pointer"
            :title="shacopied ? 'Copied!' : 'Click to copy'"
            @click="copySha(run.sha256)"
          >
            <code class="text-xs break-all text-text-primary group-hover:text-text-primary">{{ run.sha256 }}</code>
            <span class="text-text-muted group-hover:text-text-secondary shrink-0">
              <Icon :icon="shacopied ? Check : Copy" :size="16" />
            </span>
          </button>
        </td>
      </tr>
      <tr>
        <th class="px-3 py-2 text-left font-semibold text-text-secondary w-40 whitespace-nowrap border-b border-border align-top">File</th>
        <td class="px-3 py-2 border-b border-border align-top">{{ run.dataFileName }}</td>
      </tr>
      <tr v-if="run.releasedAt">
        <th class="px-3 py-2 text-left font-semibold text-text-secondary w-40 whitespace-nowrap border-b border-border align-top">Released At</th>
        <td class="px-3 py-2 border-b border-border align-top">{{ formatDate(run.releasedAt) }}</td>
      </tr>
      <tr v-if="run.operatedBy">
        <th class="px-3 py-2 text-left font-semibold text-text-secondary w-40 whitespace-nowrap border-b border-border align-top">Operated By</th>
        <td class="px-3 py-2 border-b border-border align-top">{{ run.operatedBy }}</td>
      </tr>
      <tr v-if="run.approvedBy">
        <th class="px-3 py-2 text-left font-semibold text-text-secondary w-40 whitespace-nowrap border-b border-border align-top">Approved By</th>
        <td class="px-3 py-2 border-b border-border align-top">{{ run.approvedBy }}</td>
      </tr>
      <tr v-if="run.deliveredAt">
        <th class="px-3 py-2 text-left font-semibold text-text-secondary w-40 whitespace-nowrap border-b border-border align-top">Delivered At</th>
        <td class="px-3 py-2 border-b border-border align-top">{{ formatDate(run.deliveredAt) }}</td>
      </tr>
      <tr v-if="run.deliveredBy">
        <th class="px-3 py-2 text-left font-semibold text-text-secondary w-40 whitespace-nowrap border-b border-border align-top">Delivered By</th>
        <td class="px-3 py-2 border-b border-border align-top">{{ run.deliveredBy }}</td>
      </tr>
      <tr v-if="run.importedRecordCount !== null">
        <th class="px-3 py-2 text-left font-semibold text-text-secondary w-40 whitespace-nowrap border-b border-border align-top">Imported Records</th>
        <td class="px-3 py-2 border-b border-border align-top">{{ run.importedRecordCount }}</td>
      </tr>
      <tr v-if="run.deliveryNotes">
        <th class="px-3 py-2 text-left font-semibold text-text-secondary w-40 whitespace-nowrap border-b border-border align-top">Delivery Notes</th>
        <td class="px-3 py-2 border-b border-border align-top">{{ run.deliveryNotes }}</td>
      </tr>
    </tbody>
  </table>
</template>
