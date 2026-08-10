<script setup lang="ts">
import { computed } from 'vue'
import type { PreviewResult } from '@/api/pipeline'

const props = defineProps<{
  preview: PreviewResult | null
  loading: boolean
  error: string | null
}>()
defineEmits<{ (e: 'refresh'): void }>()

const PREVIEW_MAX = 20
const previewCols = computed(() => props.preview?.columns ?? [])
const previewRows = computed(() => props.preview?.records.slice(0, PREVIEW_MAX) ?? [])

function previewVal(rec: PreviewResult['records'][number], col: string): string {
  return rec[col] ?? '—'
}
</script>

<template>
  <div class="mb-8">
    <div class="flex items-center gap-3 mb-1">
      <h2 class="m-0 text-base font-semibold text-slate-900">Preview</h2>
      <span v-if="preview" class="text-xs text-slate-500">{{ preview.recordCount >= 50 ? '50+' : preview.recordCount }} records (preview) · schema v{{ preview.schemaVersion }}</span>
      <span v-if="preview?.source === 'error'" class="inline-block bg-orange-50 border border-orange-200 rounded-full px-2 py-0.5 text-xs font-semibold text-orange-700">Preview failed</span>
      <button class="ml-auto px-2.5 py-1 border border-slate-300 rounded-md bg-white text-xs text-slate-500 cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed hover:enabled:bg-slate-50" :disabled="loading" @click="$emit('refresh')">Refresh</button>
    </div>
    <p class="text-xs text-slate-500 m-0 mb-3">Read-only view of what the next export will contain. Nothing is written to disk.</p>

    <p v-if="loading" class="text-slate-500 text-sm">Loading preview…</p>
    <p v-else-if="error" class="text-red-600 text-sm">{{ error }}</p>

    <div v-else-if="preview?.source === 'error'" class="bg-red-50 border border-red-200 rounded-md px-4 py-3">
      <p class="m-0 mb-1 text-sm text-red-700 font-semibold">{{ preview.error }}</p>
      <p class="m-0 text-xs text-red-600">Check your connection (Step 1) and make sure at least one column is enabled in Step 3.</p>
    </div>

    <template v-else-if="preview && preview.records.length > 0">
      <div class="overflow-x-auto max-h-80 overflow-y-auto border border-slate-200 rounded-md">
        <table class="w-full border-collapse text-sm">
          <thead class="sticky top-0">
            <tr>
              <th class="px-2.5 py-2 text-left bg-slate-50 font-semibold text-[0.7rem] uppercase tracking-wide border-b border-slate-200 whitespace-nowrap">#</th>
              <th v-for="col in previewCols" :key="col" class="px-2.5 py-2 text-left bg-slate-50 font-semibold text-[0.7rem] uppercase tracking-wide border-b border-slate-200 whitespace-nowrap">{{ col }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="(rec, idx) in previewRows" :key="idx" class="hover:bg-slate-50">
              <td class="px-2.5 py-1.5 border-b border-slate-200 align-middle text-center text-slate-400 text-xs w-8">{{ idx + 1 }}</td>
              <td v-for="col in previewCols" :key="col" class="px-2.5 py-1.5 border-b border-slate-200 align-middle whitespace-nowrap">{{ previewVal(rec, col) }}</td>
            </tr>
          </tbody>
        </table>
      </div>
      <p v-if="preview.records.length > PREVIEW_MAX" class="text-xs text-slate-400 mt-1.5">
        Showing first {{ PREVIEW_MAX }} rows — preview is capped at 50; the full export includes all in-scope records.
      </p>
    </template>

    <p v-else-if="preview" class="text-slate-500 text-sm">No in-scope records found.</p>
  </div>
</template>
