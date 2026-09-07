<script setup lang="ts">
import { computed } from 'vue'
import { Check, X, Clock, Truck, Ban } from 'lucide-vue-next'
import Icon from '@/components/ui/Icon.vue'
import type { Component } from 'vue'

const props = defineProps<{ status: string }>()

// Data-driven instead of a switch/ternary chain: a status maps to one (icon, color) pair here, and
// every consumer — export runs, export/import definition runs, import-run four-eyes review — funnels
// through the same table rather than each growing its own branch.
const STYLES: Record<string, { icon: Component; bg: string; text: string }> = {
  pending: { icon: Clock, bg: 'bg-warning-bg', text: 'text-warning' },
  pendingreview: { icon: Clock, bg: 'bg-warning-bg', text: 'text-warning' },
  running: { icon: Clock, bg: 'bg-warning-bg', text: 'text-warning' },
  released: { icon: Check, bg: 'bg-success-bg', text: 'text-success' },
  success: { icon: Check, bg: 'bg-success-bg', text: 'text-success' },
  failed: { icon: X, bg: 'bg-danger-bg', text: 'text-danger' },
  rejected: { icon: X, bg: 'bg-danger-bg', text: 'text-danger' },
  skipped: { icon: Ban, bg: 'bg-surface-elevated', text: 'text-text-muted' },
  delivered: { icon: Truck, bg: 'bg-info-bg', text: 'text-info' },
}
const DEFAULT_STYLE = { icon: null, bg: 'bg-surface-elevated', text: 'text-text-secondary' }

const style = computed(() => STYLES[props.status.toLowerCase()] ?? DEFAULT_STYLE)
</script>

<template>
  <span :class="['inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-semibold', style.bg, style.text]">
    <Icon v-if="style.icon" :icon="style.icon" :size="16" />{{ status }}
  </span>
</template>
