<script setup lang="ts">
withDefaults(defineProps<{ elevated?: boolean }>(), { elevated: false })
</script>

<template>
  <div
    class="rounded-lg border border-border bg-surface p-5"
    :class="elevated ? 'shadow-md' : ''"
  >
    <div v-if="$slots.header" class="mb-4">
      <slot name="header" />
    </div>
    <slot />
    <div v-if="$slots.footer" class="mt-4">
      <slot name="footer" />
    </div>
  </div>
</template>
