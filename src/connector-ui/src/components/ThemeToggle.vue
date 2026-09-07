<script setup lang="ts">
import { useTheme, type ThemePreference } from '@/composables/useTheme'

const { preference, setPreference } = useTheme()

const options: { value: ThemePreference; label: string }[] = [
  { value: 'light', label: 'Light' },
  { value: 'system', label: 'Auto' },
  { value: 'dark', label: 'Dark' },
]
</script>

<template>
  <div class="inline-flex items-center rounded-md border border-nav-border p-0.5" aria-label="Theme">
    <button
      v-for="opt in options"
      :key="opt.value"
      type="button"
      :aria-pressed="preference === opt.value"
      class="px-2 py-0.5 rounded text-[0.72rem] text-nav-text cursor-pointer transition-colors"
      :class="preference === opt.value ? '!bg-nav-hover !text-nav-text-strong' : 'hover:text-nav-text-strong'"
      @click="setPreference(opt.value)"
    >
      {{ opt.label }}
    </button>
  </div>
</template>
