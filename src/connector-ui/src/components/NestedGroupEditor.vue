<script setup lang="ts">
import { computed, ref } from 'vue'
import type { SourceColumn, SourceTable } from '@/api/connection'
import type { MappingNestedGroup, MappingNestedField } from '@/api/mapping'
import FieldPickerTable from '@/components/FieldPickerTable.vue'
import { X } from 'lucide-vue-next'
import Icon from '@/components/ui/Icon.vue'
import Alert from '@/components/ui/Alert.vue'

// Self-referencing recursive component. Vue 3 SFCs can implicitly reference themselves by
// filename, but defineOptions makes the self-registration explicit and independent of the file
// staying named exactly "NestedGroupEditor.vue".
//
// Template complexity is intentionally left unsplit here: this component supports arbitrary
// nesting depth, so its "children" block recurses into itself — pulling that block into a
// separate file would just import this file right back, a circular dependency fallow itself
// flags as worse than the complexity it'd save. The field-row and field-picker sections are
// already extracted (see FieldPickerTable.vue); this is what's left after that.
defineOptions({ name: 'NestedGroupEditor' })

const props = defineProps<{
  group: MappingNestedGroup
  availableTables: SourceTable[]
  depth: number
  // The full list of groups at this same level (including this one) — used to detect a
  // duplicate export key among siblings, which would otherwise silently overwrite a key
  // in the exported JSON. Defaults to just this group when no siblings are known.
  siblings?: MappingNestedGroup[]
}>()

const emit = defineEmits<{
  remove: []
  dirty: []
}>()

// Mirrors DynamicExportService.MaxNestedDepth on the backend — keeps the UI from ever building
// something the save endpoint would reject.
const MAX_NESTED_DEPTH = 16

// Initial open/collapsed state only — a plain ref (not a computed tracking group.relatedTable)
// so picking a related table doesn't yank the form shut out from under whoever's mid-edit.
const detailsOpen = ref(!props.group.relatedTable)

function columnsForTable(tableName: string): SourceColumn[] {
  return props.availableTables.find((t) => t.name === tableName)?.columns ?? []
}

// Related table changed: the old field list refers to columns of the previous table.
function onRelatedTableChanged() {
  props.group.fields = fieldsForTable(props.group.relatedTable)
  emit('dirty')
}

function fieldsForTable(tableName: string): MappingNestedField[] {
  return columnsForTable(tableName).map((c) => ({
    sourceField: c.name,
    targetKey: c.name,
    enabled: false,
  }))
}

function addChild() {
  props.group.children.push({
    targetKey: '',
    relatedTable: '',
    joinKey: '',
    sourceJoinKey: '',
    enabled: true,
    kind: 'object',
    fields: [],
    children: [],
  })
  emit('dirty')
}

function removeChild(idx: number) {
  props.group.children.splice(idx, 1)
  emit('dirty')
}

// One-line summary shown in the collapsed card — lets a user with several nested groups
// already configured see the structure without every field table expanded at once.
const summary = computed(() => {
  if (!props.group.relatedTable) return 'New nested group — configure below'
  const enabled = props.group.fields.filter((f) => f.enabled).length
  const shape = props.group.kind === 'array' ? 'array' : 'object'
  const key = props.group.targetKey ? `"${props.group.targetKey}": ` : ''
  return `${key}${props.group.relatedTable} (${shape}) · ${enabled}/${props.group.fields.length} fields`
})

// A brand-new, untouched group has nothing to validate yet — only start flagging once the
// user has entered a target key or related table, so an empty "+ Add Nested Group" click
// doesn't immediately show as broken.
const isPristine = computed(() => !props.group.targetKey && !props.group.relatedTable)

const hasDuplicateTargetKey = computed(() => {
  if (!props.group.targetKey) return false
  return (props.siblings ?? [props.group]).some(
    (g) => g !== props.group && g.targetKey === props.group.targetKey,
  )
})

// Structural problems that would only otherwise surface as a save error or a bad export —
// surfaced inline instead, next to the fields that cause them.
const validationIssues = computed(() => {
  if (isPristine.value) return []
  const issues: string[] = []
  if (!props.group.relatedTable) issues.push('Related table is required.')
  if (props.group.relatedTable && !props.group.joinKey) issues.push('Join column is required.')
  if (props.group.relatedTable && !props.group.sourceJoinKey) issues.push('Matches Parent Column is required.')
  if (hasDuplicateTargetKey.value) {
    issues.push(`Export key "${props.group.targetKey}" is already used by another nested group at this level.`)
  }
  return issues
})
</script>

<template>
  <div
    :class="['nested-group-card flex gap-3 items-start px-4 py-3 border rounded-lg mb-2 bg-surface', group.enabled ? 'border-brand/25 bg-brand/10' : 'border-border opacity-65']"
  >
    <div class="pt-1 shrink-0">
      <input type="checkbox" v-model="group.enabled" class="cursor-pointer w-4 h-4" @change="emit('dirty')" />
    </div>

    <details class="flex-1" :open="detailsOpen">
      <summary class="cursor-pointer select-none text-sm text-text-primary">{{ summary }}</summary>
      <div class="flex flex-col gap-2 mt-2">
        <div class="flex gap-2.5 flex-wrap">
          <div class="flex flex-col gap-1 flex-1 min-w-32">
            <label class="text-[0.7rem] font-semibold text-text-secondary uppercase tracking-wide">Export Key</label>
            <input
              class="export-key-input px-2 py-1.5 border border-border-strong rounded text-sm text-text-primary bg-surface w-full outline-none focus:border-brand"
              type="text"
              v-model="group.targetKey"
              placeholder="e.g. manufacturer"
              @input="emit('dirty')"
            />
          </div>
          <div class="flex flex-col gap-1 flex-1 min-w-36">
            <label class="text-[0.7rem] font-semibold text-text-secondary uppercase tracking-wide">Related Table</label>
            <select
              v-model="group.relatedTable"
              class="related-table-select px-2 py-1.5 border border-border-strong rounded text-sm text-text-primary bg-surface w-full"
              @change="onRelatedTableChanged"
            >
              <option value="" disabled>— select —</option>
              <option v-for="t in availableTables" :key="t.name" :value="t.name">{{ t.name }}</option>
            </select>
          </div>
          <div class="flex flex-col gap-1 flex-1 min-w-36">
            <label class="text-[0.7rem] font-semibold text-text-secondary uppercase tracking-wide">Join Column (in {{ group.relatedTable || '…' }})</label>
            <select
              v-model="group.joinKey"
              :disabled="!group.relatedTable"
              class="join-key-select px-2 py-1.5 border border-border-strong rounded text-sm text-text-primary bg-surface w-full disabled:bg-surface-elevated disabled:text-text-muted"
              @change="emit('dirty')"
            >
              <option value="" disabled>— select —</option>
              <option v-for="c in columnsForTable(group.relatedTable)" :key="c.name" :value="c.name">{{ c.name }}</option>
            </select>
          </div>
          <div class="flex flex-col gap-1 flex-1 min-w-36">
            <label class="text-[0.7rem] font-semibold text-text-secondary uppercase tracking-wide">Matches Parent Column</label>
            <input
              class="source-join-key-input px-2 py-1.5 border border-border-strong rounded text-sm text-text-primary bg-surface w-full outline-none focus:border-brand"
              type="text"
              v-model="group.sourceJoinKey"
              placeholder="e.g. id"
              @input="emit('dirty')"
            />
          </div>
          <div class="flex flex-col gap-1 w-52 shrink-0">
            <label class="text-[0.7rem] font-semibold text-text-secondary uppercase tracking-wide">Shape</label>
            <select
              v-model="group.kind"
              class="kind-select px-2 py-1.5 border border-border-strong rounded text-sm text-text-primary bg-surface w-full"
              @change="emit('dirty')"
            >
              <option value="object">Single object (1:1 lookup)</option>
              <option value="array">List of objects (1:many)</option>
            </select>
          </div>
        </div>

        <Alert v-if="validationIssues.length > 0" variant="danger" class="validation-alert py-2 px-3 text-xs">
          <ul class="list-disc pl-4 m-0">
            <li v-for="(issue, i) in validationIssues" :key="i">{{ issue }}</li>
          </ul>
        </Alert>

        <!-- Per-group field picker: which columns of the related table become object keys. -->
        <FieldPickerTable
          v-if="group.relatedTable"
          :fields="group.fields"
          :related-table="group.relatedTable"
          target-prop="targetKey"
          @dirty="emit('dirty')"
        />

        <!-- Children: further nested groups within this group's object/array shape. -->
        <div class="flex flex-col gap-1 mt-1 pl-4 border-l-2 border-border">
          <div class="flex items-center gap-2">
            <span class="text-[0.7rem] font-semibold text-text-muted uppercase tracking-wide">Nested within "{{ group.targetKey || '…' }}"</span>
            <button
              v-if="depth < MAX_NESTED_DEPTH"
              type="button"
              class="add-child-btn ml-auto px-2 py-0.5 border border-border-strong rounded text-[0.7rem] text-text-secondary bg-surface cursor-pointer hover:bg-surface-elevated"
              @click="addChild"
            >+ Add Nested Group</button>
          </div>
          <NestedGroupEditor
            v-for="(child, idx) in group.children"
            :key="idx"
            :group="child"
            :available-tables="availableTables"
            :depth="depth + 1"
            :siblings="group.children"
            @remove="removeChild(idx)"
            @dirty="emit('dirty')"
          />
        </div>
      </div>
    </details>

    <button
      class="remove-group-btn shrink-0 p-1 border border-danger rounded text-danger bg-surface leading-none cursor-pointer hover:bg-danger-bg"
      @click="emit('remove')"
      title="Remove nested group"
    ><Icon :icon="X" :size="16" /></button>
  </div>
</template>
