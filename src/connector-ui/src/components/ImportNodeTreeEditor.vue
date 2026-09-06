<script setup lang="ts">
import { computed, ref } from 'vue'
import type { SourceColumn, SourceTable } from '@/api/connection'
import type { ImportNode } from '@/api/importDefinitions'
import SuggestedRelations, { type SuggestedRelation } from '@/components/SuggestedRelations.vue'
import { findSuggestedRelations } from '@/lib/suggestedRelations'
import { blankFieldMapping } from '@/lib/exportNodeBuilders'
import { columnsAsDisabledScalarFields } from '@/lib/importNodeBuilders'
import ExportNodeMappingEditor from '@/components/ExportNodeMappingEditor.vue'
import { X } from 'lucide-vue-next'
import Icon from '@/components/ui/Icon.vue'

// The write-side mirror of ExportNodeTreeEditor.vue (import-definitions.md §5): same recursive-editor
// idea, built against ImportNode instead of ExportNode. Two differences follow directly from the data
// model, not from UI taste: there's no Filter input (ImportNode has no such field — every matched row is
// in scope, filtering happens at the correlation-key match, not here), and OnMissingChild has no picker at
// all — v1 only permits "reject" (the Slice 5 validator rejects "insert" outright, Open Decision #15), so
// offering a choice that always fails server-side would be actively misleading.
defineOptions({ name: 'ImportNodeTreeEditor' })

const props = defineProps<{
  nodes: ImportNode[]
  contextTable: string
  availableTables: SourceTable[]
  depth: number
}>()

const emit = defineEmits<{ dirty: [] }>()

// Mirrors DynamicExportService.MaxNestedDepth on the backend (ImportDefinitionEndpoints.ValidateNode
// enforces the same limit for import trees).
const MAX_NESTED_DEPTH = 16

function columnsForTable(tableName: string | null | undefined): SourceColumn[] {
  return props.availableTables.find((t) => t.name === tableName)?.columns ?? []
}

const contextColumns = computed(() => columnsForTable(props.contextTable))

function addField() {
  props.nodes.push({
    sourceKey: '',
    kind: 'scalar-field',
    targetColumn: null,
    relatedTable: null,
    joinKey: null,
    sourceJoinKey: null,
    onMissingChild: 'reject',
    mapping: blankFieldMapping(),
    children: [],
    enabled: true,
  })
  emit('dirty')
}

function addRelated(kind: 'object' | 'array', suggestion?: SuggestedRelation) {
  props.nodes.push({
    sourceKey: suggestion?.relatedTable ?? '',
    kind,
    targetColumn: null,
    relatedTable: suggestion?.relatedTable ?? '',
    joinKey: suggestion?.joinKey ?? '',
    sourceJoinKey: suggestion?.sourceJoinKey ?? '',
    onMissingChild: 'reject',
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

// Picking a related table replaces stale children (they'd refer to the previous table's columns) with
// one disabled scalar-field node per column — same "pick columns via checkbox" UX the export tree editor
// gives.
function onRelatedTableChanged(node: ImportNode) {
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
            <label class="text-[0.65rem] font-semibold text-slate-400 uppercase tracking-wide">Source key (JSON)</label>
            <input
              type="text"
              v-model="node.sourceKey"
              placeholder="e.g. status"
              aria-label="Source key"
              class="px-2 py-1 border border-slate-300 rounded text-sm text-slate-900 outline-none focus:border-slate-900"
              @input="emit('dirty')"
            />
          </div>
          <span class="text-xs bg-slate-100 text-slate-500 px-1.5 py-1 rounded-full self-end mb-1">{{ node.kind }}</span>

          <template v-if="node.kind === 'scalar-field'">
            <div class="flex flex-col gap-1 min-w-40">
              <label class="text-[0.65rem] font-semibold text-slate-400 uppercase tracking-wide">Target column ({{ contextTable }})</label>
              <select
                v-model="node.targetColumn"
                aria-label="Target column"
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
            <span class="text-xs text-slate-400 self-end mb-1.5" title="v1 only matches existing rows — a related row that doesn't resolve is rejected, never created (Open Decision #15).">
              match-only, never created
            </span>
          </template>
        </div>

        <ExportNodeMappingEditor v-if="node.kind === 'scalar-field' && node.mapping" :mapping="node.mapping" @dirty="emit('dirty')" />

        <div v-if="node.kind === 'object' || node.kind === 'array'" class="mt-1 pl-4 border-l-2 border-slate-100">
          <ImportNodeTreeEditor
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
        class="shrink-0 p-1 border border-red-200 rounded text-red-600 bg-white leading-none cursor-pointer hover:bg-red-50"
        title="Remove"
        @click="removeNode(idx)"
      ><Icon :icon="X" :size="16" /></button>
    </div>

    <SuggestedRelations
      v-if="depth < MAX_NESTED_DEPTH"
      :suggestions="topLevelSuggestions"
      :selected-table-name="contextTable"
      @add="(s) => addRelated(s.kind, s)"
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
