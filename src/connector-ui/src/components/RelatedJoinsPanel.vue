<script setup lang="ts">
import type { SourceColumn, SourceTable } from '@/api/connection'
import type { MappingRelation } from '@/api/mapping'
import SuggestedRelations, { type SuggestedRelation } from '@/components/SuggestedRelations.vue'
import RelationsSection from '@/components/RelationsSection.vue'

defineProps<{
  relations: MappingRelation[]
  relatableTables: SourceTable[]
  selectedTableColumns: SourceColumn[]
  selectedTableName: string
  suggestions: SuggestedRelation[]
}>()

const emit = defineEmits<{
  addSuggested: [s: SuggestedRelation]
  add: []
  remove: [idx: number]
  dirty: []
}>()
</script>

<template>
  <!-- Collapsed unless already configured or a join was detected -->
  <details class="mb-7" :open="relations.length > 0 || suggestions.length > 0">
    <summary class="cursor-pointer select-none text-base font-semibold text-text-primary">
      Related Table Joins <span class="text-xs font-normal text-text-muted">(optional)</span>
    </summary>
    <div class="mt-3">
      <SuggestedRelations
        :suggestions="suggestions"
        :selected-table-name="selectedTableName"
        @add="emit('addSuggested', $event)"
      />
      <RelationsSection
        :relations="relations"
        :relatable-tables="relatableTables"
        :selected-table-columns="selectedTableColumns"
        @add="emit('add')"
        @remove="emit('remove', $event)"
        @dirty="emit('dirty')"
      />
    </div>
  </details>
</template>
