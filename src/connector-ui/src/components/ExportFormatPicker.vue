<script setup lang="ts">
import HelpTooltip from '@/components/ui/HelpTooltip.vue'

defineProps<{
  modelValue: 'xlsx' | 'csv' | 'json'
}>()
const emit = defineEmits<{ 'update:modelValue': ['xlsx' | 'csv' | 'json'] }>()

const formats = [
  { id: 'xlsx', label: 'Excel (.xlsx)', desc: 'Full format with metadata row — required for the vendor Transform Map' },
  { id: 'csv', label: 'CSV (.csv)', desc: 'Plain text, comma-separated — compatible with most tools' },
  { id: 'json', label: 'JSON (.json)', desc: 'Machine-readable — useful for APIs and custom pipelines' },
] as const
</script>

<template>
  <div class="mb-7">
    <span class="inline-flex items-center gap-1.5 mb-2.5">
      <h2 class="text-base font-semibold text-text-primary m-0">Export Format</h2>
      <slot name="help">
        <HelpTooltip label="Which format should I pick?" title="Choosing an output format">
          <p>Pick <strong>Excel</strong> if the vendor's Transform Map requires it — it's the only format that carries the metadata row that tool expects.</p>
          <p>Pick <strong>CSV</strong> for the widest compatibility with spreadsheets and generic tools.</p>
          <p>Pick <strong>JSON</strong> when the receiving system is an API, or when you need nested objects/arrays (via Nested Groups) rather than flat columns.</p>
        </HelpTooltip>
      </slot>
    </span>
    <div class="flex gap-3">
      <button
        v-for="fmt in formats"
        :key="fmt.id"
        :class="['format-btn flex-1 flex flex-col gap-1 px-4 py-3 border-2 rounded-lg bg-surface cursor-pointer text-left transition-colors', modelValue === fmt.id ? 'border-brand bg-surface-elevated' : 'border-border hover:border-border-strong']"
        @click="emit('update:modelValue', fmt.id)"
      >
        <span class="text-sm font-semibold text-text-primary">{{ fmt.label }}</span>
        <span class="text-xs text-text-secondary leading-snug">{{ fmt.desc }}</span>
      </button>
    </div>
  </div>
</template>
