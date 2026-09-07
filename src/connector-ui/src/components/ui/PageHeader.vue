<script setup lang="ts">
// Consolidates the "eyebrow + <h1> + trailing actions" block repeated (with minor drift) at the top of
// every list/detail view, so it converges on the --text-page-title token instead of each view picking
// its own text-xl/arbitrary-value heading size.
withDefaults(defineProps<{ title: string; eyebrow?: string }>(), {})
</script>

<template>
  <div class="flex items-center justify-between gap-3 mb-2 flex-wrap">
    <div>
      <p v-if="eyebrow" class="text-text-secondary text-xs font-semibold uppercase tracking-wide m-0 mb-1">{{ eyebrow }}</p>
      <span class="inline-flex items-center gap-1.5">
        <h1 class="m-0 text-page-title font-semibold text-text-primary">{{ title }}</h1>
        <slot name="help" />
      </span>
    </div>
    <div v-if="$slots.actions" class="flex items-center gap-2 shrink-0">
      <slot name="actions" />
    </div>
  </div>
</template>
