<script setup lang="ts">
import { computed, ref } from 'vue'
import type { SourceColumn, SourceTable } from '@/api/connection'
import type { ExportNode } from '@/api/exportDefinitions'
import SuggestedRelations, { type SuggestedRelation } from '@/components/SuggestedRelations.vue'
import { findSuggestedRelations } from '@/lib/suggestedRelations'
import { blankFieldMapping, columnsAsDisabledScalarFields } from '@/lib/exportNodeBuilders'
import ExportNodeMappingEditor from '@/components/ExportNodeMappingEditor.vue'

// The Slice 5 tree builder (export-definitions-2.0.md §7): the single recursive editor for every
// ExportNode kind ("scalar-field" | "object" | "array"), in every output format — no more
// JSON-only gating, no more read-the-identifiers-only recovery form (see ExportNodeFieldEditor.vue,
// which this supersedes as the definition editor's tree UI once a definition has real columns to
// build from). Structurally mirrors NestedGroupEditor.vue (the legacy MappingNestedGroup tree
// editor this generalizes the *idea* of) but is kept as its own component: the two operate on
// different, incompatible tree shapes (MappingNestedGroup's flat per-group `fields` list vs.
// ExportNode's uniform recursive `children`), and the legacy `/export-schema` flow this doc's
// Non-Goals section requires stay untouched is the one component NestedGroupEditor.vue serves.
defineOptions({ name: 'ExportNodeTreeEditor' })

const props = defineProps<{
  nodes: ExportNode[]
  contextTable: string
  availableTables: SourceTable[]
  depth: number
}>()

const emit = defineEmits<{ dirty: [] }>()

// Mirrors DynamicExportService.MaxNestedDepth on the backend.
const MAX_NESTED_DEPTH = 16

function columnsForTable(tableName: string | null | undefined): SourceColumn[] {
  return props.availableTables.find((t) => t.name === tableName)?.columns ?? []
}

const contextColumns = computed(() => columnsForTable(props.contextTable))

function addField() {
  props.nodes.push({
    targetKey: '',
    kind: 'scalar-field',
    sourceField: null,
    relatedTable: null,
    joinKey: null,
    sourceJoinKey: null,
    filter: null,
    mapping: blankFieldMapping(),
    children: [],
    enabled: true,
  })
  emit('dirty')
}

function addRelated(kind: 'object' | 'array', suggestion?: SuggestedRelation) {
  props.nodes.push({
    targetKey: suggestion?.relatedTable ?? '',
    kind,
    sourceField: null,
    relatedTable: suggestion?.relatedTable ?? '',
    joinKey: suggestion?.joinKey ?? '',
    sourceJoinKey: suggestion?.sourceJoinKey ?? '',
    filter: null,
    mapping: null,
    children: [],
    enabled: true,
  })
  emit('dirty')
}

function removeNode(idx: number) {
  props.nodes.splice(idx, 1)
  emit('dirty')
}

// Picking a related table replaces stale children (they'd refer to the previous table's columns)
// with one disabled scalar-field node per column — the same "pick columns via checkbox" UX
// FieldPickerTable.vue gives the legacy editor, so a non-programmer never has to hand-type a
// SourceField (export-definitions-2.0.md §8 Usability).
function onRelatedTableChanged(node: ExportNode) {
  node.children = columnsAsDisabledScalarFields(node.relatedTable, props.availableTables)
  emit('dirty')
}

const availableTableNames = computed(() => props.availableTables.map((t) => t.name))

// Suggested relations for adding a *new* nested group at this level, keyed off contextTable.
const topLevelSuggestions = computed<SuggestedRelation[]>(() =>
  findSuggestedRelations(
    { connectionLabel: '', tables: props.availableTables },
    props.contextTable,
    props.nodes
      .filter((n) => n.kind === 'object' || n.kind === 'array')
      .map((n) => ({ relatedTable: n.relatedTable ?? '', joinKey: n.joinKey ?? '', sourceJoinKey: n.sourceJoinKey ?? '' })),
  ),
)

const showAddMenu = ref(false)
</script>

<template>
  <div class="flex flex-col gap-2">
    <div
      v-for="(node, idx) in nodes"
      :key="idx"
      :class="['flex gap-3 items-start px-4 py-3 border rounded-lg bg-white', node.enabled ? 'border-indigo-200 bg-indigo-50/30' : 'border-slate-200 opacity-70']"
      :style="{ marginLeft: `${depth * 1.25}rem` }"
    >
      <input type="checkbox" v-model="node.enabled" class="mt-1 cursor-pointer w-4 h-4 shrink-0" @change="emit('dirty')" />

      <div class="flex-1 flex flex-col gap-2">
        <div class="flex gap-2.5 flex-wrap items-end">
          <div class="flex flex-col gap-1 min-w-32">
            <label class="text-[0.65rem] font-semibold text-slate-400 uppercase tracking-wide">Export key</label>
            <input
              type="text"
              v-model="node.targetKey"
              placeholder="e.g. sku"
              aria-label="Export key"
              class="px-2 py-1 border border-slate-300 rounded text-sm text-slate-900 outline-none focus:border-slate-900"
              @input="emit('dirty')"
            />
          </div>
          <span class="text-xs bg-slate-100 text-slate-500 px-1.5 py-1 rounded-full self-end mb-1">{{ node.kind }}</span>

          <template v-if="node.kind === 'scalar-field'">
            <div class="flex flex-col gap-1 min-w-40">
              <label class="text-[0.65rem] font-semibold text-slate-400 uppercase tracking-wide">Source column ({{ contextTable }})</label>
              <select
                v-model="node.sourceField"
                aria-label="Source column"
                class="px-2 py-1 border border-slate-300 rounded text-sm text-slate-900 bg-white"
                @change="emit('dirty')"
              >
                <option :value="null" disabled>— select —</option>
                <option v-for="c in contextColumns" :key="c.name" :value="c.name">{{ c.name }}</option>
              </select>
            </div>
          </template>

          <template v-else>
            <div class="flex flex-col gap-1 min-w-36">
              <label class="text-[0.65rem] font-semibold text-slate-400 uppercase tracking-wide">Related table</label>
              <select
                v-model="node.relatedTable"
                class="px-2 py-1 border border-slate-300 rounded text-sm text-slate-900 bg-white"
                @change="onRelatedTableChanged(node)"
              >
                <option value="" disabled>— select —</option>
                <option v-for="name in availableTableNames" :key="name" :value="name">{{ name }}</option>
              </select>
            </div>
            <div class="flex flex-col gap-1 min-w-32">
              <label class="text-[0.65rem] font-semibold text-slate-400 uppercase tracking-wide">Join column (in {{ node.relatedTable || '…' }})</label>
              <select
                v-model="node.joinKey"
                :disabled="!node.relatedTable"
                class="px-2 py-1 border border-slate-300 rounded text-sm text-slate-900 bg-white disabled:bg-slate-50"
                @change="emit('dirty')"
              >
                <option value="" disabled>— select —</option>
                <option v-for="c in columnsForTable(node.relatedTable)" :key="c.name" :value="c.name">{{ c.name }}</option>
              </select>
            </div>
            <div class="flex flex-col gap-1 min-w-32">
              <label class="text-[0.65rem] font-semibold text-slate-400 uppercase tracking-wide">Matches column (in {{ contextTable }})</label>
              <select
                v-model="node.sourceJoinKey"
                class="px-2 py-1 border border-slate-300 rounded text-sm text-slate-900 bg-white"
                @change="emit('dirty')"
              >
                <option value="" disabled>— select —</option>
                <option v-for="c in contextColumns" :key="c.name" :value="c.name">{{ c.name }}</option>
              </select>
            </div>
            <div class="flex flex-col gap-1 w-40">
              <label class="text-[0.65rem] font-semibold text-slate-400 uppercase tracking-wide">Shape</label>
              <select
                v-model="node.kind"
                class="px-2 py-1 border border-slate-300 rounded text-sm text-slate-900 bg-white"
                @change="emit('dirty')"
              >
                <option value="object">Single object (1:1)</option>
                <option value="array">List (1:N)</option>
              </select>
            </div>
            <div class="flex flex-col gap-1 min-w-40 flex-1">
              <label class="text-[0.65rem] font-semibold text-slate-400 uppercase tracking-wide">Filter (optional)</label>
              <input
                type="text"
                v-model="node.filter"
                placeholder="e.g. is_active = true"
                class="px-2 py-1 border border-slate-300 rounded text-sm text-slate-900 font-mono outline-none focus:border-slate-900"
                @input="emit('dirty')"
              />
            </div>
          </template>
        </div>

        <ExportNodeMappingEditor v-if="node.kind === 'scalar-field' && node.mapping" :mapping="node.mapping" @dirty="emit('dirty')" />

        <div v-if="node.kind === 'object' || node.kind === 'array'" class="mt-1 pl-4 border-l-2 border-slate-100">
          <ExportNodeTreeEditor
            :nodes="node.children"
            :context-table="node.relatedTable ?? ''"
            :available-tables="availableTables"
            :depth="depth + 1"
            @dirty="emit('dirty')"
          />
        </div>
      </div>

      <button
        type="button"
        class="shrink-0 px-2 py-1 border border-red-200 rounded text-red-600 bg-white text-base leading-none cursor-pointer hover:bg-red-50"
        title="Remove"
        @click="removeNode(idx)"
      >×</button>
    </div>

    <SuggestedRelations
      v-if="depth < MAX_NESTED_DEPTH"
      :suggestions="topLevelSuggestions"
      :selected-table-name="contextTable"
      @add="(s) => addRelated('array', s)"
    />

    <div v-if="depth < MAX_NESTED_DEPTH" class="relative">
      <div class="flex gap-2">
        <button
          type="button"
          class="px-3 py-1.5 border border-slate-300 rounded-md bg-white text-sm text-slate-700 cursor-pointer hover:bg-slate-50"
          @click="addField"
        >+ Add Field</button>
        <button
          type="button"
          class="px-3 py-1.5 border border-slate-300 rounded-md bg-white text-sm text-slate-700 cursor-pointer hover:bg-slate-50"
          @click="showAddMenu = !showAddMenu"
        >+ Add Related Entity</button>
      </div>
      <div v-if="showAddMenu" class="mt-1 flex gap-2">
        <button
          type="button"
          class="px-2.5 py-1 border border-slate-200 rounded text-xs text-slate-600 bg-slate-50 cursor-pointer hover:bg-slate-100"
          @click="addRelated('object'); showAddMenu = false"
        >Single object (1:1)</button>
        <button
          type="button"
          class="px-2.5 py-1 border border-slate-200 rounded text-xs text-slate-600 bg-slate-50 cursor-pointer hover:bg-slate-100"
          @click="addRelated('array'); showAddMenu = false"
        >List (1:N)</button>
      </div>
    </div>
  </div>
</template>
