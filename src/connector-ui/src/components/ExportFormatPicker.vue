<script setup lang="ts">
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
    <h2 class="text-base font-semibold text-slate-900 mb-2.5">Export Format</h2>
    <div class="flex gap-3">
      <button
        v-for="fmt in formats"
        :key="fmt.id"
        :class="['format-btn flex-1 flex flex-col gap-1 px-4 py-3 border-2 rounded-lg bg-white cursor-pointer text-left transition-colors', modelValue === fmt.id ? 'border-slate-900 bg-slate-50' : 'border-slate-200 hover:border-slate-400']"
        @click="emit('update:modelValue', fmt.id)"
      >
        <span class="text-sm font-semibold text-slate-900">{{ fmt.label }}</span>
        <span class="text-xs text-slate-500 leading-snug">{{ fmt.desc }}</span>
      </button>
    </div>
  </div>
</template>
