<script setup lang="ts">
import type { Component } from 'vue'
import Icon from '@/components/ui/Icon.vue'

// Shared "nothing here yet" block for lists/tables — replaces the one-off
// `<div class="text-text-secondary text-sm mt-4">No … yet.</div>` lines duplicated across views with a
// consistent, slightly more prominent treatment, while staying just as easy to drop in.
withDefaults(defineProps<{ icon?: Component; title: string; description?: string }>(), {})
</script>

<template>
  <div class="flex flex-col items-center gap-2 rounded-lg border border-dashed border-border px-6 py-10 text-center">
    <span v-if="icon" class="text-text-muted"><Icon :icon="icon" :size="24" /></span>
    <p class="m-0 text-sm font-semibold text-text-primary">{{ title }}</p>
    <p v-if="description" class="m-0 text-sm text-text-secondary max-w-sm">{{ description }}</p>
    <div v-if="$slots.default" class="mt-2"><slot /></div>
  </div>
</template>
