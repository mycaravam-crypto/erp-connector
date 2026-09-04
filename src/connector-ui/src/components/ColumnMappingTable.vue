<script setup lang="ts">
import { computed } from 'vue'
import type { SourceColumn } from '@/api/connection'
import type { MappingField } from '@/api/mapping'

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
      <h2 class="text-base font-semibold text-text-primary m-0">Columns</h2>
      <span class="text-xs font-medium text-text-secondary bg-surface-elevated px-2 py-0.5 rounded-full">{{ enabledCount }} / {{ fields.length }} selected</span>
      <div class="ml-auto flex gap-1.5">
        <button class="px-2.5 py-1 border border-border-strong rounded text-xs text-text-secondary bg-surface cursor-pointer hover:bg-surface-elevated" @click="selectAll">Select All</button>
        <button class="px-2.5 py-1 border border-border-strong rounded text-xs text-text-secondary bg-surface cursor-pointer hover:bg-surface-elevated" @click="deselectAll">Deselect All</button>
      </div>
    </div>
    <table class="col-table w-full border-collapse text-sm">
      <thead>
        <tr>
          <th class="px-2.5 py-2 text-center bg-surface-elevated font-semibold text-[0.72rem] uppercase tracking-wide text-text-secondary border-b border-border w-12">Export</th>
          <th class="px-2.5 py-2 text-left bg-surface-elevated font-semibold text-[0.72rem] uppercase tracking-wide text-text-secondary border-b border-border w-8">#</th>
          <th class="px-2.5 py-2 text-left bg-surface-elevated font-semibold text-[0.72rem] uppercase tracking-wide text-text-secondary border-b border-border">Source Column</th>
          <th class="px-2.5 py-2 text-left bg-surface-elevated font-semibold text-[0.72rem] uppercase tracking-wide text-text-secondary border-b border-border">Export As (target name)</th>
          <th class="px-2.5 py-2 text-left bg-surface-elevated font-semibold text-[0.72rem] uppercase tracking-wide text-text-secondary border-b border-border">Type</th>
          <th class="px-2.5 py-2 text-left bg-surface-elevated font-semibold text-[0.72rem] uppercase tracking-wide text-text-secondary border-b border-border">PK</th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="(field, idx) in fields"
          :key="field.sourceName"
          :class="field.enabled ? 'bg-success-bg' : 'bg-surface-elevated opacity-60'"
        >
          <td class="px-2.5 py-2 border-b border-border align-middle text-center">
            <input type="checkbox" v-model="field.enabled" @change="emit('dirty')" />
          </td>
          <td class="px-2.5 py-2 border-b border-border align-middle text-center text-text-muted text-xs">{{ idx + 1 }}</td>
          <td class="px-2.5 py-2 border-b border-border align-middle">
            <code :class="['text-sm font-semibold', field.enabled ? 'text-text-primary' : 'text-text-muted']">{{ field.sourceName }}</code>
          </td>
          <td class="px-2.5 py-2 border-b border-border align-middle min-w-40">
            <input
              class="export-as-input w-full px-1.5 py-1 border border-border-strong rounded text-xs font-mono text-text-primary bg-surface box-border outline-none focus:border-brand placeholder-text-muted disabled:bg-surface-elevated"
              type="text"
              :placeholder="field.sourceName"
              v-model="field.targetName"
              :disabled="!field.enabled"
              @input="emit('dirty')"
            />
          </td>
          <td class="px-2.5 py-2 border-b border-border align-middle">
            <span class="inline-block px-1.5 py-0.5 bg-surface-elevated border border-border rounded text-xs text-text-secondary whitespace-nowrap">
              {{ columnMap[field.sourceName]?.type ?? '' }}
            </span>
          </td>
          <td class="px-2.5 py-2 border-b border-border align-middle">
            <span v-if="columnMap[field.sourceName]?.primaryKey" class="pk-badge text-[0.65rem] font-bold bg-info-bg text-info px-1.5 py-0.5 rounded">PK</span>
            <span v-else class="text-text-muted">—</span>
          </td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
