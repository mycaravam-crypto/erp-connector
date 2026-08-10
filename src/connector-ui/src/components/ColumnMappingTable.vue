<script setup lang="ts">
import { computed } from 'vue'
import type { SourceColumn } from '@/api/connection'
import type { MappingField } from '@/api/erp'

const props = defineProps<{
  fields: MappingField[]
  columnMap: Record<string, SourceColumn>
}>()
const emit = defineEmits<{ dirty: [] }>()

const enabledCount = computed(() => props.fields.filter((f) => f.enabled).length)

function selectAll() {
  props.fields.forEach((f) => {
    f.enabled = true
  })
  emit('dirty')
}

function deselectAll() {
  props.fields.forEach((f) => {
    f.enabled = false
  })
  emit('dirty')
}
</script>

<template>
  <div class="mb-7">
    <div class="flex items-center gap-2 mb-2.5 flex-wrap">
      <h2 class="text-base font-semibold text-slate-900 m-0">Columns</h2>
      <span class="text-xs font-medium text-slate-500 bg-slate-100 px-2 py-0.5 rounded-full">{{ enabledCount }} / {{ fields.length }} selected</span>
      <div class="ml-auto flex gap-1.5">
        <button class="px-2.5 py-1 border border-slate-300 rounded text-xs text-slate-600 bg-white cursor-pointer hover:bg-slate-50" @click="selectAll">Select All</button>
        <button class="px-2.5 py-1 border border-slate-300 rounded text-xs text-slate-600 bg-white cursor-pointer hover:bg-slate-50" @click="deselectAll">Deselect All</button>
      </div>
    </div>
    <table class="col-table w-full border-collapse text-sm">
      <thead>
        <tr>
          <th class="px-2.5 py-2 text-center bg-slate-50 font-semibold text-[0.72rem] uppercase tracking-wide text-slate-500 border-b border-slate-200 w-12">Export</th>
          <th class="px-2.5 py-2 text-left bg-slate-50 font-semibold text-[0.72rem] uppercase tracking-wide text-slate-500 border-b border-slate-200 w-8">#</th>
          <th class="px-2.5 py-2 text-left bg-slate-50 font-semibold text-[0.72rem] uppercase tracking-wide text-slate-500 border-b border-slate-200">Source Column</th>
          <th class="px-2.5 py-2 text-left bg-slate-50 font-semibold text-[0.72rem] uppercase tracking-wide text-slate-500 border-b border-slate-200">Export As (target name)</th>
          <th class="px-2.5 py-2 text-left bg-slate-50 font-semibold text-[0.72rem] uppercase tracking-wide text-slate-500 border-b border-slate-200">Type</th>
          <th class="px-2.5 py-2 text-left bg-slate-50 font-semibold text-[0.72rem] uppercase tracking-wide text-slate-500 border-b border-slate-200">PK</th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="(field, idx) in fields"
          :key="field.sourceName"
          :class="field.enabled ? 'bg-green-50' : 'bg-neutral-50 opacity-60'"
        >
          <td class="px-2.5 py-2 border-b border-slate-200 align-middle text-center">
            <input type="checkbox" v-model="field.enabled" @change="emit('dirty')" />
          </td>
          <td class="px-2.5 py-2 border-b border-slate-200 align-middle text-center text-slate-400 text-xs">{{ idx + 1 }}</td>
          <td class="px-2.5 py-2 border-b border-slate-200 align-middle">
            <code :class="['text-sm font-semibold', field.enabled ? 'text-slate-900' : 'text-slate-400']">{{ field.sourceName }}</code>
          </td>
          <td class="px-2.5 py-2 border-b border-slate-200 align-middle min-w-40">
            <input
              class="export-as-input w-full px-1.5 py-1 border border-slate-300 rounded text-xs font-mono text-slate-900 bg-white box-border outline-none focus:border-slate-900 placeholder-slate-400 disabled:bg-slate-50"
              type="text"
              :placeholder="field.sourceName"
              v-model="field.targetName"
              :disabled="!field.enabled"
              @input="emit('dirty')"
            />
          </td>
          <td class="px-2.5 py-2 border-b border-slate-200 align-middle">
            <span class="inline-block px-1.5 py-0.5 bg-slate-100 border border-slate-200 rounded text-xs text-slate-500 whitespace-nowrap">
              {{ columnMap[field.sourceName]?.type ?? '' }}
            </span>
          </td>
          <td class="px-2.5 py-2 border-b border-slate-200 align-middle">
            <span v-if="columnMap[field.sourceName]?.primaryKey" class="pk-badge text-[0.65rem] font-bold bg-blue-100 text-blue-800 px-1.5 py-0.5 rounded">PK</span>
            <span v-else class="text-slate-200">—</span>
          </td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
