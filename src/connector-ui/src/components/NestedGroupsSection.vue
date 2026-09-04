<script setup lang="ts">
import type { SourceTable } from '@/api/connection'
import type { MappingNestedGroup } from '@/api/mapping'
import NestedGroupEditor from '@/components/NestedGroupEditor.vue'

defineProps<{
  groups: MappingNestedGroup[]
  availableTables: SourceTable[]
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
      <h2 class="text-base font-semibold text-text-primary m-0">Nested JSON Structure</h2>
      <button
        class="add-nested-group-btn px-3 py-1.5 border border-border-strong rounded-md bg-surface text-sm text-text-secondary cursor-pointer whitespace-nowrap hover:bg-surface-elevated"
        @click="emit('add')"
      >+ Add Nested Group</button>
    </div>
    <p class="text-sm text-text-secondary mb-3 leading-snug">
      Applies to JSON export only. Embed a related table as a single object (1:1 lookup, e.g. <em>manufacturer</em>)
      or an array of objects (1:many, e.g. <em>addresses</em>) — nested groups can themselves contain further
      nested groups, so an array can hold objects that have their own nested arrays.
    </p>

    <div v-if="groups.length === 0" class="text-sm text-text-muted px-4 py-3 border border-dashed border-border-strong rounded-md text-center">
      No nested groups configured — JSON export will use the flat field/relation mapping above.
    </div>

    <NestedGroupEditor
      v-for="(group, idx) in groups"
      :key="idx"
      :group="group"
      :available-tables="availableTables"
      :depth="1"
      @remove="emit('remove', idx)"
      @dirty="emit('dirty')"
    />
  </div>
</template>
