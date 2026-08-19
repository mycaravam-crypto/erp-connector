<script setup lang="ts">
import type { SourceColumn, SourceTable } from '@/api/connection'
import type { MappingRelation, MappingRelationField } from '@/api/mapping'

const props = defineProps<{
  relation: MappingRelation
  relatableTables: SourceTable[]
  selectedTableColumns: SourceColumn[]
}>()

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

function selectAllFields() {
  props.relation.fields.forEach((f) => {
    f.enabled = true
  })
  emit('dirty')
}

function deselectAllFields() {
  props.relation.fields.forEach((f) => {
    f.enabled = false
  })
  emit('dirty')
}
</script>

<template>
  <div
    :class="['relation-card flex gap-3 items-start px-4 py-3 border rounded-lg mb-2 bg-white', relation.enabled ? 'border-blue-200 bg-sky-50' : 'border-slate-200 opacity-65']"
  >
    <div class="pt-1 shrink-0">
      <input type="checkbox" v-model="relation.enabled" class="cursor-pointer w-4 h-4" @change="emit('dirty')" />
    </div>

    <div class="flex-1 flex flex-col gap-2">
      <div class="flex gap-2.5 flex-wrap">
        <div class="flex flex-col gap-1 flex-1 min-w-36">
          <label class="text-[0.7rem] font-semibold text-slate-500 uppercase tracking-wide whitespace-nowrap">Related Table</label>
          <select v-model="relation.relatedTable" class="px-2 py-1.5 border border-slate-300 rounded text-sm text-slate-900 bg-white w-full" @change="onRelatedTableChanged">
            <option value="" disabled>— select —</option>
            <option v-for="t in relatableTables" :key="t.name" :value="t.name">{{ t.name }}</option>
          </select>
        </div>
        <div class="flex flex-col gap-1 flex-1 min-w-36">
          <label class="text-[0.7rem] font-semibold text-slate-500 uppercase tracking-wide whitespace-nowrap">Source Column</label>
          <select v-model="relation.sourceJoinKey" class="px-2 py-1.5 border border-slate-300 rounded text-sm text-slate-900 bg-white w-full" @change="emit('dirty')">
            <option value="" disabled>— select —</option>
            <option v-for="c in selectedTableColumns" :key="c.name" :value="c.name">{{ c.name }}{{ c.primaryKey ? ' (PK)' : '' }}</option>
          </select>
        </div>
        <div class="flex flex-col gap-1 flex-1 min-w-36">
          <label class="text-[0.7rem] font-semibold text-slate-500 uppercase tracking-wide whitespace-nowrap">Join Column (in {{ relation.relatedTable || '…' }})</label>
          <select v-model="relation.joinKey" :disabled="!relation.relatedTable" class="px-2 py-1.5 border border-slate-300 rounded text-sm text-slate-900 bg-white w-full disabled:bg-slate-50 disabled:text-slate-400" @change="emit('dirty')">
            <option value="" disabled>— select —</option>
            <option v-for="c in getTableColumns(relation.relatedTable)" :key="c.name" :value="c.name">{{ c.name }}</option>
          </select>
        </div>
        <div class="flex flex-col gap-1 flex-1 min-w-36">
          <label class="text-[0.7rem] font-semibold text-slate-500 uppercase tracking-wide whitespace-nowrap">Flatten Strategy</label>
          <select v-model="relation.flattenStrategy" class="px-2 py-1.5 border border-slate-300 rounded text-sm text-slate-900 bg-white w-full" @change="emit('dirty')">
            <option value="string_join">String Join (concatenate with delimiter)</option>
            <option value="array">Array (comma-separated list)</option>
          </select>
        </div>
        <div v-if="relation.flattenStrategy === 'string_join'" class="flex flex-col gap-1 w-24 shrink-0">
          <label class="text-[0.7rem] font-semibold text-slate-500 uppercase tracking-wide whitespace-nowrap">Delimiter</label>
          <input class="px-2 py-1.5 border border-slate-300 rounded text-sm text-slate-900 bg-white w-full outline-none focus:border-slate-900" type="text" v-model="relation.delimiter" placeholder=", " @input="emit('dirty')" />
        </div>
      </div>

      <!-- Per-relation field picker: which columns of the related table to pull, and what to rename them to. -->
      <div v-if="relation.relatedTable" class="border border-slate-200 rounded-md overflow-hidden">
        <div class="flex items-center gap-2 px-2.5 py-1.5 bg-slate-50 border-b border-slate-200">
          <span class="text-[0.7rem] font-semibold text-slate-500 uppercase tracking-wide">Fields from {{ relation.relatedTable }}</span>
          <span class="text-[0.7rem] text-slate-400">{{ relation.fields.filter((f) => f.enabled).length }} / {{ relation.fields.length }} selected</span>
          <div class="ml-auto flex gap-1.5">
            <button type="button" class="rel-select-all-btn px-2 py-0.5 border border-slate-300 rounded text-[0.7rem] text-slate-600 bg-white cursor-pointer hover:bg-slate-100" @click="selectAllFields">Select All</button>
            <button type="button" class="rel-deselect-all-btn px-2 py-0.5 border border-slate-300 rounded text-[0.7rem] text-slate-600 bg-white cursor-pointer hover:bg-slate-100" @click="deselectAllFields">Deselect All</button>
          </div>
        </div>
        <table class="rel-fields-table w-full border-collapse text-sm">
          <tbody>
            <tr
              v-for="rf in relation.fields"
              :key="rf.sourceField"
              :class="rf.enabled ? 'bg-green-50' : 'bg-white opacity-60'"
            >
              <td class="px-2.5 py-1.5 border-b border-slate-100 align-middle text-center w-8">
                <input type="checkbox" v-model="rf.enabled" @change="emit('dirty')" />
              </td>
              <td class="px-2.5 py-1.5 border-b border-slate-100 align-middle">
                <code :class="['text-xs font-semibold', rf.enabled ? 'text-slate-900' : 'text-slate-400']">{{ rf.sourceField }}</code>
              </td>
              <td class="px-2.5 py-1.5 border-b border-slate-100 align-middle min-w-32">
                <input
                  class="rel-field-export-as-input w-full px-1.5 py-1 border border-slate-300 rounded text-xs font-mono text-slate-900 bg-white box-border outline-none focus:border-slate-900 placeholder-slate-400 disabled:bg-slate-50"
                  type="text"
                  :placeholder="rf.sourceField"
                  v-model="rf.targetField"
                  :disabled="!rf.enabled"
                  @input="emit('dirty')"
                />
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>

    <button
      class="rel-remove-btn shrink-0 px-2 py-1 border border-red-200 rounded text-red-600 bg-white text-base leading-none cursor-pointer hover:bg-red-50"
      @click="emit('remove')"
      title="Remove relation"
    >×</button>
  </div>
</template>
