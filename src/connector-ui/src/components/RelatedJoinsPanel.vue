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
  convertToNestedGroup: [idx: number]
}>()
</script>

<template>
  <!-- Advanced/legacy path — collapsed by default, except when an existing mapping already has
       relations configured, so nothing already saved is hidden. Suggested joins alone (with no
       relations configured yet) no longer force this open, since Nested JSON is the primary path. -->
  <details class="legacy-relations-details mb-7" :open="relations.length > 0">
    <summary class="cursor-pointer select-none text-base font-semibold text-text-primary">
      Related Table Joins <span class="text-xs font-normal text-text-muted">(advanced — flat/legacy exports)</span>
    </summary>
    <p class="text-xs text-text-secondary mt-1 mb-3 leading-snug">
      Only needed for xlsx/csv exports without a Nested JSON Structure — for JSON export, use a
      <strong>Nested Group</strong> above instead (table joins are ignored there).
    </p>
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
        @convert-to-nested-group="emit('convertToNestedGroup', $event)"
      />
    </div>
  </details>
</template>
