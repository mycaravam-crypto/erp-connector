<script setup lang="ts">
// Generic status/count pill. Consolidates the ad-hoc "rounded-full px-2 py-0.5 text-xs" spans that were
// hand-rolled per view (count summaries, preview panels, ...) — StatusBadge stays the icon+label variant
// for run/import statuses specifically; this is the plain no-icon pill for everything else.
withDefaults(defineProps<{ variant?: 'neutral' | 'success' | 'warning' | 'danger' | 'info' | 'brand' }>(), {
  variant: 'neutral',
})

const variants: Record<string, string> = {
  neutral: 'bg-surface-elevated text-text-secondary',
  success: 'bg-success-bg text-success',
  warning: 'bg-warning-bg text-warning',
  danger: 'bg-danger-bg text-danger',
  info: 'bg-info-bg text-info',
  brand: 'bg-brand/10 text-brand',
}
</script>

<template>
  <span
    class="inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium"
    :class="variants[variant]"
  ><slot /></span>
</template>
