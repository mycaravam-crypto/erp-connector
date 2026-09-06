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
  pending: { icon: Clock, bg: 'bg-yellow-100', text: 'text-yellow-800' },
  pendingreview: { icon: Clock, bg: 'bg-yellow-100', text: 'text-yellow-800' },
  running: { icon: Clock, bg: 'bg-yellow-100', text: 'text-yellow-800' },
  released: { icon: Check, bg: 'bg-green-100', text: 'text-green-800' },
  success: { icon: Check, bg: 'bg-green-100', text: 'text-green-800' },
  failed: { icon: X, bg: 'bg-red-100', text: 'text-red-800' },
  rejected: { icon: X, bg: 'bg-red-100', text: 'text-red-800' },
  skipped: { icon: Ban, bg: 'bg-slate-100', text: 'text-slate-500' },
  delivered: { icon: Truck, bg: 'bg-blue-100', text: 'text-blue-800' },
}
const DEFAULT_STYLE = { icon: null, bg: 'bg-slate-100', text: 'text-slate-600' }

const style = computed(() => STYLES[props.status.toLowerCase()] ?? DEFAULT_STYLE)
</script>

<template>
  <span :class="['inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-semibold', style.bg, style.text]">
    <Icon v-if="style.icon" :icon="style.icon" :size="16" />{{ status }}
  </span>
</template>
