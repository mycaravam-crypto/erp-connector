<script setup lang="ts">
import { computed, ref } from 'vue'
import type { SourceColumn, SourceTable } from '@/api/connection'
import type { MappingRelation, MappingRelationField } from '@/api/mapping'
import FieldPickerTable from '@/components/FieldPickerTable.vue'

const props = defineProps<{
  relation: MappingRelation
  relatableTables: SourceTable[]
  selectedTableColumns: SourceColumn[]
}>()

// Initial open/collapsed state only — a plain ref (not a computed tracking relation.relatedTable)
// so picking a related table doesn't yank the form shut out from under whoever's mid-edit.
const detailsOpen = ref(!props.relation.relatedTable)

const emit = defineEmits<{
  remove: []
  dirty: []
}>()

function getTableColumns(tableName: string): SourceColumn[] {
  return props.relatableTables.find((t) => t.name === tableName)?.columns ?? []
}

function fieldsForTable(tableName: string): MappingRelationField[] {
  return getTableColumns(tableName).map((c) => ({
    sourceField: c.name,
    targetField: c.name,
    enabled: false,
  }))
}

function onRelatedTableChanged() {
  props.relation.fields = fieldsForTable(props.relation.relatedTable)
  emit('dirty')
}

// One-line summary shown in the collapsed card — lets a user with several relations
// already configured see what's there without every field table expanded at once.
const summary = computed(() => {
  if (!props.relation.relatedTable) return 'New relation — configure below'
  const enabled = props.relation.fields.filter((f) => f.enabled).length
  const join = props.relation.joinKey ? ` on ${props.relation.sourceJoinKey} = ${props.relation.joinKey}` : ''
  return `${props.relation.relatedTable}${join} · ${enabled}/${props.relation.fields.length} fields`
})
</script>

<template>
  <div
    :class="['relation-card flex gap-3 items-start px-4 py-3 border rounded-lg mb-2 bg-white', relation.enabled ? 'border-blue-200 bg-sky-50' : 'border-slate-200 opacity-65']"
  >
    <div class="pt-1 shrink-0">
      <input type="checkbox" v-model="relation.enabled" class="cursor-pointer w-4 h-4" @change="emit('dirty')" />
    </div>

    <details class="flex-1" :open="detailsOpen">
      <summary class="cursor-pointer select-none text-sm text-slate-700">{{ summary }}</summary>
      <div class="flex flex-col gap-2 mt-2">
        <div class="flex gap-2.5 flex-wrap">
          <div class="flex flex-col gap-1 flex-1 min-w-36">
            <label class="text-[0.7rem] font-semibold text-slate-500 uppercase tracking-wide">Related Table</label>
            <select v-model="relation.relatedTable" class="px-2 py-1.5 border border-slate-300 rounded text-sm text-slate-900 bg-white w-full" @change="onRelatedTableChanged">
              <option value="" disabled>— select —</option>
              <option v-for="t in relatableTables" :key="t.name" :value="t.name">{{ t.name }}</option>
            </select>
          </div>
          <div class="flex flex-col gap-1 flex-1 min-w-36">
            <label class="text-[0.7rem] font-semibold text-slate-500 uppercase tracking-wide">Source Column</label>
            <select v-model="relation.sourceJoinKey" class="px-2 py-1.5 border border-slate-300 rounded text-sm text-slate-900 bg-white w-full" @change="emit('dirty')">
              <option value="" disabled>— select —</option>
              <option v-for="c in selectedTableColumns" :key="c.name" :value="c.name">{{ c.name }}{{ c.primaryKey ? ' (PK)' : '' }}</option>
            </select>
          </div>
          <div class="flex flex-col gap-1 flex-1 min-w-36">
            <label class="text-[0.7rem] font-semibold text-slate-500 uppercase tracking-wide">Join Column (in {{ relation.relatedTable || '…' }})</label>
            <select v-model="relation.joinKey" :disabled="!relation.relatedTable" class="px-2 py-1.5 border border-slate-300 rounded text-sm text-slate-900 bg-white w-full disabled:bg-slate-50 disabled:text-slate-400" @change="emit('dirty')">
              <option value="" disabled>— select —</option>
              <option v-for="c in getTableColumns(relation.relatedTable)" :key="c.name" :value="c.name">{{ c.name }}</option>
            </select>
          </div>
          <div class="flex flex-col gap-1 flex-1 min-w-36">
            <label class="text-[0.7rem] font-semibold text-slate-500 uppercase tracking-wide">Flatten Strategy</label>
            <select v-model="relation.flattenStrategy" class="px-2 py-1.5 border border-slate-300 rounded text-sm text-slate-900 bg-white w-full" @change="emit('dirty')">
              <option value="string_join">String Join (concatenate with delimiter)</option>
              <option value="array">Array (comma-separated list)</option>
            </select>
          </div>
          <div v-if="relation.flattenStrategy === 'string_join'" class="flex flex-col gap-1 w-24 shrink-0">
            <label class="text-[0.7rem] font-semibold text-slate-500 uppercase tracking-wide">Delimiter</label>
            <input class="px-2 py-1.5 border border-slate-300 rounded text-sm text-slate-900 bg-white w-full outline-none focus:border-slate-900" type="text" v-model="relation.delimiter" placeholder=", " @input="emit('dirty')" />
          </div>
        </div>

        <!-- Per-relation field picker: which columns of the related table to pull, and what to rename them to. -->
        <FieldPickerTable
          v-if="relation.relatedTable"
          :fields="relation.fields"
          :related-table="relation.relatedTable"
          target-prop="targetField"
          @dirty="emit('dirty')"
        />
      </div>
    </details>

    <button
      class="rel-remove-btn shrink-0 px-2 py-1 border border-red-200 rounded text-red-600 bg-white text-base leading-none cursor-pointer hover:bg-red-50"
      @click="emit('remove')"
      title="Remove relation"
    >×</button>
  </div>
</template>
