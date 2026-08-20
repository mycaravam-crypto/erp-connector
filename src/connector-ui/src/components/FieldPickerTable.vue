<script setup lang="ts" generic="T extends { sourceField: string; enabled: boolean }">
// Shared by RelationCard and NestedGroupEditor — both need an identical checkbox+rename table
// over "columns of a related table", differing only in which property holds the rename (relations
// call it targetField, nested groups call it targetKey). `targetProp` picks that property
// type-safely per caller instead of forcing the two mapping shapes to match.
const props = defineProps<{
  fields: T[]
  relatedTable: string
  targetProp: keyof T
}>()
const emit = defineEmits<{ dirty: [] }>()

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

function onTargetInput(f: T, e: Event) {
  f[props.targetProp] = (e.target as HTMLInputElement).value as T[keyof T]
  emit('dirty')
}
</script>

<template>
  <div class="border border-slate-200 rounded-md overflow-hidden">
    <div class="flex items-center gap-2 px-2.5 py-1.5 bg-slate-50 border-b border-slate-200">
      <span class="text-[0.7rem] font-semibold text-slate-500 uppercase tracking-wide">Fields from {{ relatedTable }}</span>
      <span class="text-[0.7rem] text-slate-400">{{ fields.filter((f) => f.enabled).length }} / {{ fields.length }} selected</span>
      <div class="ml-auto flex gap-1.5">
        <button type="button" class="field-picker-select-all-btn px-2 py-0.5 border border-slate-300 rounded text-[0.7rem] text-slate-600 bg-white cursor-pointer hover:bg-slate-100" @click="selectAll">Select All</button>
        <button type="button" class="field-picker-deselect-all-btn px-2 py-0.5 border border-slate-300 rounded text-[0.7rem] text-slate-600 bg-white cursor-pointer hover:bg-slate-100" @click="deselectAll">Deselect All</button>
      </div>
    </div>
    <table class="field-picker-table w-full border-collapse text-sm">
      <tbody>
        <tr
          v-for="f in fields"
          :key="f.sourceField"
          :class="f.enabled ? 'bg-green-50' : 'bg-white opacity-60'"
        >
          <td class="px-2.5 py-1.5 border-b border-slate-100 align-middle text-center w-8">
            <input type="checkbox" v-model="f.enabled" @change="emit('dirty')" />
          </td>
          <td class="px-2.5 py-1.5 border-b border-slate-100 align-middle">
            <code :class="['text-xs font-semibold', f.enabled ? 'text-slate-900' : 'text-slate-400']">{{ f.sourceField }}</code>
          </td>
          <td class="px-2.5 py-1.5 border-b border-slate-100 align-middle min-w-32">
            <input
              class="field-picker-target-input w-full px-1.5 py-1 border border-slate-300 rounded text-xs font-mono text-slate-900 bg-white box-border outline-none focus:border-slate-900 placeholder-slate-400 disabled:bg-slate-50"
              type="text"
              :placeholder="f.sourceField"
              :value="f[targetProp]"
              :disabled="!f.enabled"
              @input="onTargetInput(f, $event)"
            />
          </td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
