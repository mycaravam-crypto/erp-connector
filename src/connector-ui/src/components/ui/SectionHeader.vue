<script setup lang="ts">
// The sub-heading counterpart to PageHeader — for the <h2>-level section titles ("Presets", "Allowed
// Writable Columns", ...) that sit above a card or toolbar within a view, converged on the
// --text-section-heading token.
withDefaults(defineProps<{ title: string }>(), {})
</script>

<template>
  <div class="flex items-center justify-between gap-2 flex-wrap">
    <h2 class="m-0 text-section-heading font-semibold text-text-primary">{{ title }}</h2>
    <div v-if="$slots.actions" class="flex items-center gap-2 shrink-0">
      <slot name="actions" />
    </div>
  </div>
</template>
