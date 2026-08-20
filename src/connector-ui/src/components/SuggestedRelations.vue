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
    <h2 class="text-base font-semibold text-slate-900 mb-1">Suggested Relations</h2>
    <p class="text-sm text-slate-500 mb-3 leading-snug">
      Detected from foreign keys in the source schema.
    </p>
    <div class="flex flex-col gap-2">
      <div
        v-for="s in suggestions"
        :key="`${s.relatedTable}.${s.joinKey}`"
        class="suggested-relation-card flex items-center justify-between gap-3 px-4 py-2.5 border border-dashed border-blue-300 bg-blue-50 rounded-lg"
      >
        <code class="text-sm text-slate-700">{{ s.relatedTable }}.{{ s.joinKey }} → {{ selectedTableName }}.{{ s.sourceJoinKey }}</code>
        <button
          class="suggested-add-btn px-3 py-1 border border-blue-300 rounded-md bg-white text-sm text-blue-700 cursor-pointer whitespace-nowrap shrink-0 hover:bg-blue-100"
          @click="emit('add', s)"
        >+ Add</button>
      </div>
    </div>
  </div>
</template>
