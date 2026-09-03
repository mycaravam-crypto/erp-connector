<script setup lang="ts">
import { computed } from 'vue'
import type { FieldMapping } from '@/api/exportDefinitions'

// Inline editor for one scalar-field node's FieldMapping (export-definitions-2.0.md §5): rename is
// the node's own TargetKey input (owned by the caller), so this only covers the four FieldMapping
// members — transform, transform argument, null-fallback, and data-type coercion target. Mirrors
// Connector.Core.DynamicExport.FieldTransform/FieldDataType exactly (see ExportNode.cs) so every
// value this can emit is one the backend validator already accepts.
const props = defineProps<{
  mapping: FieldMapping
}>()
const emit = defineEmits<{ dirty: [] }>()

const transformArgLabel = computed(() => {
  switch (props.mapping.transform) {
    case 'dateFormat':
      return 'Format string (e.g. yyyy-MM-dd)'
    case 'constant':
      return 'Constant value'
    default:
      return null
  }
})

function onTransformChanged() {
  if (!transformArgLabel.value) props.mapping.transformArg = null
  emit('dirty')
}
</script>

<template>
  <div class="flex gap-2.5 flex-wrap mt-1.5 pl-4 border-l-2 border-slate-100">
    <div class="flex flex-col gap-1 min-w-36">
      <label class="text-[0.65rem] font-semibold text-slate-400 uppercase tracking-wide">Transform</label>
      <select
        v-model="mapping.transform"
        class="px-2 py-1 border border-slate-300 rounded text-sm text-slate-900 bg-white"
        @change="onTransformChanged"
      >
        <option value="none">None</option>
        <option value="uppercase">Uppercase</option>
        <option value="lowercase">Lowercase</option>
        <option value="trim">Trim</option>
        <option value="dateFormat">Date format</option>
        <option value="constant">Constant</option>
      </select>
    </div>

    <div v-if="transformArgLabel" class="flex flex-col gap-1 min-w-40 flex-1">
      <label class="text-[0.65rem] font-semibold text-slate-400 uppercase tracking-wide">{{ transformArgLabel }}</label>
      <input
        type="text"
        v-model="mapping.transformArg"
        class="px-2 py-1 border border-slate-300 rounded text-sm text-slate-900 outline-none focus:border-slate-900"
        @input="emit('dirty')"
      />
    </div>

    <div class="flex flex-col gap-1 min-w-40 flex-1">
      <label class="text-[0.65rem] font-semibold text-slate-400 uppercase tracking-wide">Default (if null)</label>
      <input
        type="text"
        v-model="mapping.defaultValue"
        placeholder="none"
        class="px-2 py-1 border border-slate-300 rounded text-sm text-slate-900 outline-none focus:border-slate-900"
        @input="emit('dirty')"
      />
    </div>

    <div class="flex flex-col gap-1 min-w-28">
      <label class="text-[0.65rem] font-semibold text-slate-400 uppercase tracking-wide">Data type</label>
      <select
        v-model="mapping.dataType"
        class="px-2 py-1 border border-slate-300 rounded text-sm text-slate-900 bg-white"
        @change="emit('dirty')"
      >
        <option value="string">String</option>
        <option value="number">Number</option>
        <option value="boolean">Boolean</option>
        <option value="date">Date</option>
      </select>
    </div>
  </div>
</template>
