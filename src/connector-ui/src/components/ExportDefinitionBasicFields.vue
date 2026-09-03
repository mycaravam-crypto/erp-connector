<script setup lang="ts">
import type { SourceTable } from '@/api/connection'
import type { ExportDefinition } from '@/api/exportDefinitions'
import ExportFormatPicker from '@/components/ExportFormatPicker.vue'
import ExportScheduleField from '@/components/ExportScheduleField.vue'

const props = defineProps<{
  definition: ExportDefinition
  availableTables: SourceTable[]
  /** Disables the root-table picker once children reference it — changing tables out from under an
   * already-built tree would silently invalidate every SourceField/RelatedTable in it. */
  rootTableLocked: boolean
}>()
const emit = defineEmits<{ 'root-table-changed': [] }>()

function onRootTableChanged() {
  if (!props.rootTableLocked) emit('root-table-changed')
}
</script>

<template>
  <div class="flex flex-col gap-3 mb-5">
    <div class="flex items-center gap-2">
      <label class="text-sm text-slate-500 w-28 shrink-0">Name</label>
      <input
        type="text"
        v-model="definition.name"
        aria-label="Name"
        class="flex-1 px-2.5 py-1.5 border border-slate-300 rounded-md text-sm text-slate-900 outline-none focus:border-slate-900"
      />
    </div>
    <div class="flex items-center gap-2">
      <label class="text-sm text-slate-500 w-28 shrink-0">Description</label>
      <input
        type="text"
        v-model="definition.description"
        placeholder="optional"
        aria-label="Description"
        class="flex-1 px-2.5 py-1.5 border border-slate-300 rounded-md text-sm text-slate-900 outline-none focus:border-slate-900"
      />
    </div>
    <div class="flex items-center gap-2">
      <label class="text-sm text-slate-500 w-28 shrink-0">Root table</label>
      <select
        v-if="availableTables.length > 0"
        v-model="definition.rootTable"
        :disabled="rootTableLocked"
        aria-label="Root table"
        class="flex-1 px-2.5 py-1.5 border border-slate-300 rounded-md text-sm text-slate-900 font-mono bg-white disabled:bg-slate-50 disabled:text-slate-400"
        @change="onRootTableChanged"
      >
        <option value="" disabled>— select —</option>
        <option v-for="t in availableTables" :key="t.name" :value="t.name">{{ t.name }}</option>
      </select>
      <input
        v-else
        type="text"
        v-model="definition.rootTable"
        placeholder="e.g. systemconfiguration"
        aria-label="Root table"
        class="flex-1 px-2.5 py-1.5 border border-slate-300 rounded-md text-sm text-slate-900 font-mono outline-none focus:border-slate-900"
      />
      <span v-if="rootTableLocked" class="text-xs text-slate-400">clear all fields to change</span>
    </div>
    <div class="flex items-center gap-2">
      <label class="text-sm text-slate-500 w-28 shrink-0">Enabled</label>
      <input type="checkbox" v-model="definition.isEnabled" class="cursor-pointer" />
      <span class="text-xs text-slate-500">Scheduled runs only fire for enabled definitions.</span>
    </div>
    <ExportScheduleField v-model="definition.schedule" />
  </div>

  <ExportFormatPicker v-model="definition.outputFormat as 'xlsx' | 'csv' | 'json'" />
</template>
