<script setup lang="ts">
import type { SourceColumn, SourceTable } from '@/api/connection'
import type { MappingRelation } from '@/api/mapping'
import RelationCard from '@/components/RelationCard.vue'

defineProps<{
  relations: MappingRelation[]
  relatableTables: SourceTable[]
  selectedTableColumns: SourceColumn[]
}>()
const emit = defineEmits<{
  add: []
  remove: [idx: number]
  dirty: []
}>()
</script>

<template>
  <div class="mb-7">
    <div class="flex items-center justify-between mb-2">
      <h2 class="text-base font-semibold text-slate-900 m-0">Related Table Joins</h2>
      <button
        class="add-btn px-3 py-1.5 border border-slate-300 rounded-md bg-white text-sm text-slate-500 cursor-pointer whitespace-nowrap hover:bg-slate-50"
        @click="emit('add')"
      >+ Add Relation</button>
    </div>
    <p class="text-sm text-slate-500 mb-3 leading-snug">
      Add 1:N joins to pull one or more columns from a related table into the export row, each independently renamed.
      Use <em>String Join</em> to concatenate values, or <em>Array</em> to comma-separate them.
    </p>

    <div v-if="relations.length === 0" class="text-sm text-slate-400 px-4 py-3 border border-dashed border-slate-300 rounded-md text-center">
      No relations configured.
    </div>

    <RelationCard
      v-for="(rel, idx) in relations"
      :key="idx"
      :relation="rel"
      :relatable-tables="relatableTables"
      :selected-table-columns="selectedTableColumns"
      @remove="emit('remove', idx)"
      @dirty="emit('dirty')"
    />
  </div>
</template>
