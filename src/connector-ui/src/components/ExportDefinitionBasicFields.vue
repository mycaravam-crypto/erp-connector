<script setup lang="ts">
import type { ExportDefinition } from '@/api/exportDefinitions'
import ExportFormatPicker from '@/components/ExportFormatPicker.vue'

defineProps<{
  definition: ExportDefinition
}>()
</script>

<template>
  <div class="flex flex-col gap-3 mb-5">
    <div class="flex items-center gap-2">
      <label class="text-sm text-slate-500 w-28 shrink-0">Name</label>
      <input
        type="text"
        v-model="definition.name"
        class="flex-1 px-2.5 py-1.5 border border-slate-300 rounded-md text-sm text-slate-900 outline-none focus:border-slate-900"
      />
    </div>
    <div class="flex items-center gap-2">
      <label class="text-sm text-slate-500 w-28 shrink-0">Root table</label>
      <input
        type="text"
        v-model="definition.rootTable"
        placeholder="e.g. systemconfiguration"
        class="flex-1 px-2.5 py-1.5 border border-slate-300 rounded-md text-sm text-slate-900 font-mono outline-none focus:border-slate-900"
      />
    </div>
    <div class="flex items-center gap-2">
      <label class="text-sm text-slate-500 w-28 shrink-0">Enabled</label>
      <input type="checkbox" v-model="definition.isEnabled" class="cursor-pointer" />
      <span class="text-xs text-slate-500">Scheduled runs only fire for enabled definitions.</span>
    </div>
  </div>

  <ExportFormatPicker v-model="definition.outputFormat as 'xlsx' | 'csv' | 'json'" />
</template>
