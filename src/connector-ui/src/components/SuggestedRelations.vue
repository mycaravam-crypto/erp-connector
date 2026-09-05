<script setup lang="ts">
export interface SuggestedRelation {
  relatedTable: string
  joinKey: string
  sourceJoinKey: string
  /** Which shape this FK naturally suggests: a forward FK (this table's own column pointing at
   * relatedTable's key) is a 1:1 lookup ("object"); a reverse FK (relatedTable's column pointing
   * back at this table) is a 1:N collection ("array"). */
  kind: 'object' | 'array'
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
        <div class="flex flex-col gap-0.5">
          <code class="text-sm text-text-primary">{{ s.relatedTable }}.{{ s.joinKey }} → {{ selectedTableName }}.{{ s.sourceJoinKey }}</code>
          <span class="text-xs text-text-secondary">{{ s.kind === 'object' ? 'single object (1:1)' : 'list (1:N)' }}</span>
        </div>
        <button
          class="suggested-add-btn px-3 py-1 border border-info rounded-md bg-surface text-sm text-info cursor-pointer whitespace-nowrap shrink-0 hover:bg-info-bg"
          @click="emit('add', s)"
        >+ Add</button>
      </div>
    </div>
  </div>
</template>
