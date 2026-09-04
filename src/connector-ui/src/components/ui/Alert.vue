<script setup lang="ts">
withDefaults(defineProps<{ variant?: 'success' | 'warning' | 'danger' | 'info'; title?: string }>(), {
  variant: 'info',
})

const bgClasses: Record<string, string> = {
  success: 'bg-success-bg border-success/25',
  warning: 'bg-warning-bg border-warning/25',
  danger: 'bg-danger-bg border-danger/25',
  info: 'bg-info-bg border-info/25',
}
const accentClasses: Record<string, string> = {
  success: 'text-success',
  warning: 'text-warning',
  danger: 'text-danger',
  info: 'text-info',
}
</script>

<template>
  <div class="flex gap-3 rounded-lg border px-4 py-3 text-sm" :class="bgClasses[variant]" role="alert">
    <span v-if="$slots.icon" class="shrink-0 mt-0.5" :class="accentClasses[variant]">
      <slot name="icon" />
    </span>
    <div class="min-w-0 flex-1 text-text-primary">
      <p v-if="title" class="font-semibold m-0" :class="$slots.default ? 'mb-0.5' : ''">{{ title }}</p>
      <div :class="title ? 'text-text-secondary' : ''"><slot /></div>
    </div>
  </div>
</template>
