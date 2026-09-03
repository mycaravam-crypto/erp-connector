<script setup lang="ts">
import { computed } from 'vue'

// export-definitions-2.0.md §6: "UI offers presets (Manual/Hourly/Daily/Weekly) plus an advanced
// free-text cron field." modelValue is the raw ExportDefinition.Schedule (a 5-field cron string, or
// null for manual-only) — this never invents a second source of truth for it.
const PRESETS: Record<string, string | null> = {
  manual: null,
  hourly: '0 * * * *',
  daily: '0 6 * * *',
  weekly: '0 6 * * 1',
}

const props = defineProps<{
  modelValue: string | null
}>()
const emit = defineEmits<{ 'update:modelValue': [value: string | null] }>()

const selectedPreset = computed(() => {
  const match = Object.entries(PRESETS).find(([, cron]) => cron === props.modelValue)
  return match ? match[0] : 'custom'
})

function onPresetChanged(e: Event) {
  const preset = (e.target as HTMLSelectElement).value
  if (preset === 'custom') {
    emit('update:modelValue', props.modelValue ?? '0 * * * *')
  } else {
    emit('update:modelValue', PRESETS[preset] ?? null)
  }
}
</script>

<template>
  <div class="flex items-center gap-2">
    <label class="text-sm text-slate-500 w-28 shrink-0">Schedule</label>
    <select
      :value="selectedPreset"
      aria-label="Schedule preset"
      class="px-2.5 py-1.5 border border-slate-300 rounded-md text-sm text-slate-900 bg-white"
      @change="onPresetChanged"
    >
      <option value="manual">Manual only</option>
      <option value="hourly">Hourly</option>
      <option value="daily">Daily (06:00 UTC)</option>
      <option value="weekly">Weekly (Mon 06:00 UTC)</option>
      <option value="custom">Custom cron…</option>
    </select>
    <input
      v-if="selectedPreset === 'custom'"
      type="text"
      :value="modelValue"
      placeholder="5-field cron, e.g. 0 */4 * * *"
      class="flex-1 px-2.5 py-1.5 border border-slate-300 rounded-md text-sm text-slate-900 font-mono outline-none focus:border-slate-900"
      @input="emit('update:modelValue', ($event.target as HTMLInputElement).value)"
    />
    <span v-else class="text-xs text-slate-400">hourly minimum granularity</span>
  </div>
</template>
