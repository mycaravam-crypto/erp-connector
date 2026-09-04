<script setup lang="ts">
import { computed } from 'vue'
import type { PreviewResult } from '@/api/pipeline'
import FlatPreviewTable from './FlatPreviewTable.vue'
import NestedPreviewList from './NestedPreviewList.vue'

const props = defineProps<{
  preview: PreviewResult | null
  loading: boolean
  error: string | null
}>()
defineEmits<{ (e: 'refresh'): void }>()

const PREVIEW_MAX = 20
const previewRows = computed(() => props.preview?.records.slice(0, PREVIEW_MAX) ?? [])
const nestedPreviewRows = computed(() => props.preview?.nestedRecords?.slice(0, PREVIEW_MAX) ?? [])
const recordCountLabel = computed(() => {
  const preview = props.preview
  if (!preview) return ''
  return preview.recordCount >= 50 ? '50+' : String(preview.recordCount)
})

type PreviewState = 'loading' | 'fetch-error' | 'preview-error' | 'nested' | 'flat' | 'empty' | 'none'

// Single decision point for which body to render, so the template itself is a flat
// dispatch over one enum instead of nested/compound conditions.
const state = computed<PreviewState>(() => {
  if (props.loading) return 'loading'
  if (props.error) return 'fetch-error'
  const preview = props.preview
  if (!preview) return 'none'
  if (preview.source === 'error') return 'preview-error'
  if (preview.source === 'dynamic-nested') return 'nested'
  if (preview.records.length > 0) return 'flat'
  return 'empty'
})
</script>

<template>
  <div class="mb-8">
    <div class="flex items-center gap-3 mb-1">
      <h2 class="m-0 text-base font-semibold text-text-primary">Preview</h2>
      <span v-if="preview" class="text-xs text-text-secondary">{{ recordCountLabel }} records (preview) · schema v{{ preview.schemaVersion }}</span>
      <span v-if="state === 'preview-error'" class="inline-block bg-warning-bg border border-warning rounded-full px-2 py-0.5 text-xs font-semibold text-warning">Preview failed</span>
      <button class="ml-auto px-2.5 py-1 border border-border-strong rounded-md bg-surface text-xs text-text-secondary cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed hover:enabled:bg-surface-elevated" :disabled="loading" @click="$emit('refresh')">Refresh</button>
    </div>
    <p class="text-xs text-text-secondary m-0 mb-3">Read-only view of what the next export will contain. Nothing is written to disk.</p>

    <p v-if="state === 'loading'" class="text-text-secondary text-sm">Loading preview…</p>
    <p v-else-if="state === 'fetch-error'" class="text-danger text-sm">{{ error }}</p>

    <div v-else-if="state === 'preview-error'" class="bg-danger-bg border border-danger rounded-md px-4 py-3">
      <p class="m-0 mb-1 text-sm text-danger font-semibold">{{ preview!.error }}</p>
      <p class="m-0 text-xs text-danger">Check your connection (Step 1) and make sure at least one column is enabled in Step 3.</p>
    </div>

    <NestedPreviewList
      v-else-if="state === 'nested'"
      :records="nestedPreviewRows"
      :total-count="preview!.recordCount"
      :max="PREVIEW_MAX"
    />

    <FlatPreviewTable
      v-else-if="state === 'flat'"
      :columns="preview!.columns"
      :rows="previewRows"
      :total-count="preview!.records.length"
      :max="PREVIEW_MAX"
    />

    <p v-else-if="state === 'empty'" class="text-text-secondary text-sm">No in-scope records found.</p>
  </div>
</template>
