<script setup lang="ts">
export interface SuggestedRelation {
  relatedTable: string
  joinKey: string
  sourceJoinKey: string
}

defineProps<{
  suggestions: SuggestedRelation[]
  selectedTableName: string
}>()
const emit = defineEmits<{ add: [s: SuggestedRelation] }>()
</script>

<template>
  <div v-if="suggestions.length > 0" class="mb-5">
    <h2 class="text-base font-semibold text-text-primary mb-1">Suggested Relations</h2>
    <p class="text-sm text-text-secondary mb-3 leading-snug">
      Detected from foreign keys in the source schema.
    </p>
    <div class="flex flex-col gap-2">
      <div
        v-for="s in suggestions"
        :key="`${s.relatedTable}.${s.joinKey}`"
        class="suggested-relation-card flex items-center justify-between gap-3 px-4 py-2.5 border border-dashed border-info bg-info-bg rounded-lg"
      >
        <code class="text-sm text-text-primary">{{ s.relatedTable }}.{{ s.joinKey }} → {{ selectedTableName }}.{{ s.sourceJoinKey }}</code>
        <button
          class="suggested-add-btn px-3 py-1 border border-info rounded-md bg-surface text-sm text-info cursor-pointer whitespace-nowrap shrink-0 hover:bg-info-bg"
          @click="emit('add', s)"
        >+ Add</button>
      </div>
    </div>
  </div>
</template>
