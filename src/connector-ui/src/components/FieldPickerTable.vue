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
  <div class="border border-border rounded-md overflow-hidden">
    <div class="flex items-center gap-2 px-2.5 py-1.5 bg-surface-elevated border-b border-border">
      <span class="text-[0.7rem] font-semibold text-text-secondary uppercase tracking-wide">Fields from {{ relatedTable }}</span>
      <span class="text-[0.7rem] text-text-muted">{{ fields.filter((f) => f.enabled).length }} / {{ fields.length }} selected</span>
      <div class="ml-auto flex gap-1.5">
        <button type="button" class="field-picker-select-all-btn px-2 py-0.5 border border-border-strong rounded text-[0.7rem] text-text-secondary bg-surface cursor-pointer hover:bg-surface-elevated" @click="selectAll">Select All</button>
        <button type="button" class="field-picker-deselect-all-btn px-2 py-0.5 border border-border-strong rounded text-[0.7rem] text-text-secondary bg-surface cursor-pointer hover:bg-surface-elevated" @click="deselectAll">Deselect All</button>
      </div>
    </div>
    <div class="overflow-x-auto">
      <table class="field-picker-table w-full border-collapse text-sm">
        <tbody>
          <tr
            v-for="f in fields"
            :key="f.sourceField"
            class="border-b border-border last:border-0 transition-colors"
            :class="f.enabled ? 'bg-success-bg hover:bg-success-bg' : 'opacity-60 hover:bg-surface-elevated'"
          >
            <td class="px-3 py-1.5 align-middle text-center w-8">
              <input type="checkbox" v-model="f.enabled" @change="emit('dirty')" />
            </td>
            <td class="px-3 py-1.5 align-middle">
              <code :class="['text-xs font-semibold', f.enabled ? 'text-text-primary' : 'text-text-muted']">{{ f.sourceField }}</code>
            </td>
            <td class="px-3 py-1.5 align-middle min-w-32">
              <input
                class="field-picker-target-input w-full px-1.5 py-1 border border-border-strong rounded text-xs font-mono text-text-primary bg-surface box-border outline-none focus:border-brand placeholder-text-muted disabled:bg-surface-elevated"
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
  </div>
</template>
