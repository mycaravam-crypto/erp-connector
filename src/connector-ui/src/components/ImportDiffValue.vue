<script setup lang="ts">
// One old/new value cell's contents, shared by both columns of ImportPlanDiffTable.vue — split out purely
// to keep that table's own template under fallow's complexity gate, not because either side has behavior
// of its own beyond the null/empty check and which style applies.
defineProps<{ value: string | null; variant: 'old' | 'new' }>()
</script>

<template>
  <span v-if="value === null" class="italic text-text-muted">(empty)</span>
  <span v-else :class="variant === 'old' ? 'line-through text-text-secondary' : 'text-success font-semibold'">{{ value }}</span>
</template>
