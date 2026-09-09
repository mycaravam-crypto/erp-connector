<script setup lang="ts">
import type { Component } from 'vue'
import { Sun, Monitor, Moon } from 'lucide-vue-next'
import Icon from '@/components/ui/Icon.vue'
import { useTheme, type ThemePreference } from '@/composables/useTheme'

const { preference, setPreference } = useTheme()

// Icon-only — the text labels ("Light"/"Auto"/"Dark") took up nav space the top bar doesn't
// have to spare. Each button keeps an aria-label/title so the meaning isn't icon-only for
// screen readers or on hover.
const options: { value: ThemePreference; label: string; icon: Component }[] = [
  { value: 'light', label: 'Light', icon: Sun },
  { value: 'system', label: 'Auto', icon: Monitor },
  { value: 'dark', label: 'Dark', icon: Moon },
]
</script>

<template>
  <div class="inline-flex items-center rounded-md border border-nav-border p-0.5" aria-label="Theme">
    <button
      v-for="opt in options"
      :key="opt.value"
      type="button"
      :aria-pressed="preference === opt.value"
      :aria-label="opt.label"
      :title="opt.label"
      class="flex items-center justify-center p-1.5 rounded text-nav-text cursor-pointer transition-colors"
      :class="preference === opt.value ? '!bg-nav-hover !text-nav-text-strong' : 'hover:text-nav-text-strong'"
      @click="setPreference(opt.value)"
    >
      <Icon :icon="opt.icon" :size="16" />
    </button>
  </div>
</template>
