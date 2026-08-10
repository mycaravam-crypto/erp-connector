<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import { useRouter, onBeforeRouteLeave } from 'vue-router'
import { getSourceSchema, type SourceTable, type SourceColumn } from '@/api/connection'
import {
  getExportMapping,
  saveExportMapping,
  type MappingField,
  type MappingRelation,
  type MappingRelationField,
  type MappingNestedGroup,
  type ExportJsonWrapperConfig,
  type ExportMappingConfig,
} from '@/api/erp'
import PresetsToolbar from '@/components/PresetsToolbar.vue'
import RelationsSection from '@/components/RelationsSection.vue'
import ExportFormatPicker from '@/components/ExportFormatPicker.vue'
import ColumnMappingTable from '@/components/ColumnMappingTable.vue'
import JsonEnvelopeEditor from '@/components/JsonEnvelopeEditor.vue'
import SuggestedRelations from '@/components/SuggestedRelations.vue'
import NestedGroupsSection from '@/components/NestedGroupsSection.vue'

const router = useRouter()

// ── Source schema ──────────────────────────────────────────────────────────────
const sourceSchema = ref<{ connectionLabel: string; tables: SourceTable[] } | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)

// ── Mapping state ──────────────────────────────────────────────────────────────
const selectedTable = ref('')
const fields = ref<MappingField[]>([])
const relations = ref<MappingRelation[]>([])
const nestedGroups = ref<MappingNestedGroup[]>([])
const jsonWrapper = ref<ExportJsonWrapperConfig | null>(null)
const saving = ref(false)
const saveError = ref<string | null>(null)
const saved = ref(false)
const dirty = ref(false)

// ── Presets ────────────────────────────────────────────────────────────────────
// Builds the config object from current mapping state; shared by preset-save and mapping-save,
// which both persist the same shape to different endpoints.
function buildMappingConfig(): ExportMappingConfig {
  return {
    sourceTable: selectedTable.value,
    fields: fields.value.map((f) => ({
      sourceName: f.sourceName,
      targetName: f.targetName.trim() || f.sourceName,
      enabled: f.enabled,
    })),
    relations: relations.value,
    nestedGroups: nestedGroups.value,
    jsonWrapper: jsonWrapper.value,
  }
}

function onApplyPreset(cfg: ExportMappingConfig) {
  fields.value = cfg.fields.map((f) => ({ ...f }))
  relations.value = cfg.relations.map(cloneRelation)
  nestedGroups.value = (cfg.nestedGroups ?? []).map(cloneNestedGroup)
  jsonWrapper.value = cloneWrapper(cfg.jsonWrapper)
  snapshotToCache(cfg.sourceTable)
  selectedTable.value = cfg.sourceTable
  saved.value = false
  dirty.value = true
}

// ── Format ─────────────────────────────────────────────────────────────────────
const FORMAT_KEY = 'connector_export_format'
const selectedFormat = ref<'xlsx' | 'csv' | 'json'>(
  (localStorage.getItem(FORMAT_KEY) as 'xlsx' | 'csv' | 'json') ?? 'xlsx',
)

function setFormat(fmt: 'xlsx' | 'csv' | 'json') {
  selectedFormat.value = fmt
  localStorage.setItem(FORMAT_KEY, fmt)
}

// ── Computed helpers ───────────────────────────────────────────────────────────
const selectedTableColumns = computed<SourceColumn[]>(() => {
  if (!sourceSchema.value || !selectedTable.value) return []
  return sourceSchema.value.tables.find((t) => t.name === selectedTable.value)?.columns ?? []
})

const selectedTableColumnMap = computed<Record<string, SourceColumn>>(() =>
  Object.fromEntries(selectedTableColumns.value.map((c) => [c.name, c])),
)

const sourcePkColumn = computed(
  () => selectedTableColumns.value.find((c) => c.primaryKey)?.name ?? '',
)

// Tables a relation can join to — every table except the primary selected one.
const relatableTables = computed<SourceTable[]>(() =>
  (sourceSchema.value?.tables ?? []).filter((t) => t.name !== selectedTable.value),
)

function getTableColumns(tableName: string): SourceColumn[] {
  return sourceSchema.value?.tables.find((t) => t.name === tableName)?.columns ?? []
}

function cloneRelation(r: MappingRelation): MappingRelation {
  return { ...r, fields: (r.fields ?? []).map((f) => ({ ...f })) }
}

function cloneNestedGroup(g: MappingNestedGroup): MappingNestedGroup {
  return {
    ...g,
    fields: (g.fields ?? []).map((f) => ({ ...f })),
    children: (g.children ?? []).map(cloneNestedGroup),
  }
}

function cloneWrapper(w: ExportJsonWrapperConfig | null): ExportJsonWrapperConfig | null {
  if (!w) return null
  return { ...w, metadataFields: (w.metadataFields ?? []).map((f) => ({ ...f })) }
}

// Default field picker for a relation's related table: every column, unchecked, renamed to itself.
function fieldsForTable(tableName: string): MappingRelationField[] {
  return getTableColumns(tableName).map((c) => ({
    sourceField: c.name,
    targetField: c.name,
    enabled: false,
  }))
}

// ── Per-table state cache ──────────────────────────────────────────────────────
// Keeps field/relation/nested-group edits alive when the user switches between tables.
// jsonWrapper is envelope-level (not tied to a source table), so it's intentionally NOT cached here.
const tableCache = new Map<
  string,
  { fields: MappingField[]; relations: MappingRelation[]; nestedGroups: MappingNestedGroup[] }
>()

function snapshotToCache(tableName: string) {
  if (!tableName) return
  tableCache.set(tableName, {
    fields: fields.value.map((f) => ({ ...f })),
    relations: relations.value.map(cloneRelation),
    nestedGroups: nestedGroups.value.map(cloneNestedGroup),
  })
}

// When selectedTable changes (user picks a new table):
//   • save current edits for the outgoing table
//   • restore cached edits for the incoming table, or initialize defaults
watch(selectedTable, (newTable, oldTable) => {
  if (!newTable || newTable === oldTable) return
  snapshotToCache(oldTable)
  const cached = tableCache.get(newTable)
  if (cached) {
    fields.value = cached.fields.map((f) => ({ ...f }))
    relations.value = cached.relations.map(cloneRelation)
    nestedGroups.value = cached.nestedGroups.map(cloneNestedGroup)
  } else {
    const cols = sourceSchema.value?.tables.find((t) => t.name === newTable)?.columns ?? []
    fields.value = cols.map((col) => ({
      sourceName: col.name,
      targetName: col.name,
      enabled: col.primaryKey,
    }))
    relations.value = []
    nestedGroups.value = []
  }
  saved.value = false
  dirty.value = true
})

// ── Suggested relations (from FK metadata) ─────────────────────────────────────
interface SuggestedRelation {
  relatedTable: string
  joinKey: string
  sourceJoinKey: string
}

const suggestedRelations = computed<SuggestedRelation[]>(() => {
  if (!sourceSchema.value || !selectedTable.value) return []

  const suggestions: SuggestedRelation[] = []
  for (const t of sourceSchema.value.tables) {
    if (t.name === selectedTable.value) continue
    for (const c of t.columns) {
      if (c.foreignKeyTable === selectedTable.value && c.foreignKeyColumn) {
        suggestions.push({ relatedTable: t.name, joinKey: c.name, sourceJoinKey: c.foreignKeyColumn })
      }
    }
  }

  return suggestions.filter(
    (s) =>
      !relations.value.some(
        (r) =>
          r.relatedTable === s.relatedTable &&
          r.joinKey === s.joinKey &&
          r.sourceJoinKey === s.sourceJoinKey,
      ),
  )
})

function addSuggestedRelation(s: SuggestedRelation) {
  relations.value.push({
    relatedTable: s.relatedTable,
    joinKey: s.joinKey,
    sourceJoinKey: s.sourceJoinKey,
    enabled: true,
    flattenStrategy: 'string_join',
    delimiter: ', ',
    fields: fieldsForTable(s.relatedTable),
  })
  saved.value = false
  dirty.value = true
}

// ── Relation management ────────────────────────────────────────────────────────
function addRelation() {
  relations.value.push({
    relatedTable: '',
    joinKey: '',
    sourceJoinKey: sourcePkColumn.value,
    enabled: true,
    flattenStrategy: 'string_join',
    delimiter: ', ',
    fields: [],
  })
  saved.value = false
  dirty.value = true
}

function removeRelation(idx: number) {
  relations.value.splice(idx, 1)
  saved.value = false
  dirty.value = true
}

// ── Nested JSON structure (JSON export only) ────────────────────────────────────
function addNestedGroup() {
  nestedGroups.value.push({
    targetKey: '',
    relatedTable: '',
    joinKey: '',
    sourceJoinKey: sourcePkColumn.value,
    enabled: true,
    kind: 'object',
    fields: [],
    children: [],
  })
  saved.value = false
  dirty.value = true
}

function removeNestedGroup(idx: number) {
  nestedGroups.value.splice(idx, 1)
  saved.value = false
  dirty.value = true
}

function markDirty() {
  saved.value = false
  dirty.value = true
}

// ── Navigation guard ───────────────────────────────────────────────────────────
onBeforeRouteLeave(() => {
  if (dirty.value) {
    return window.confirm('You have unsaved mapping changes. Leave anyway?')
  }
})

// ── Save ───────────────────────────────────────────────────────────────────────
async function saveMapping(): Promise<boolean> {
  saveError.value = null
  saved.value = false

  if (!selectedTable.value) {
    saveError.value = 'Please select a source table.'
    return false
  }

  const config = buildMappingConfig()

  saving.value = true
  const result = await saveExportMapping(config)
  saving.value = false

  if (!result.ok) {
    saveError.value = result.error ?? 'Failed to save mapping.'
    return false
  }

  saved.value = true
  dirty.value = false
  return true
}

async function proceed() {
  const ok = await saveMapping()
  if (ok) router.push({ name: 'exports' })
}

// ── Load ───────────────────────────────────────────────────────────────────────
async function load() {
  loading.value = true
  error.value = null
  saveError.value = null
  saved.value = false

  try {
    const [schema, existingMapping] = await Promise.all([getSourceSchema(), getExportMapping()])

    if (!schema) {
      error.value = 'Could not load source schema. Configure a database connection first.'
      return
    }

    sourceSchema.value = schema

    if (existingMapping) {
      fields.value = existingMapping.fields.map((f) => ({ ...f }))
      relations.value = existingMapping.relations.map(cloneRelation)
      nestedGroups.value = (existingMapping.nestedGroups ?? []).map(cloneNestedGroup)
      jsonWrapper.value = cloneWrapper(existingMapping.jsonWrapper)
      // Seed the cache so switching away and back restores the saved state.
      snapshotToCache(existingMapping.sourceTable)
      selectedTable.value = existingMapping.sourceTable
    }
  } catch {
    error.value = 'Could not reach the API. Is the backend service running?'
  } finally {
    loading.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="max-w-4xl">
    <div class="flex items-center gap-3 mb-2">
      <span class="bg-slate-900 text-slate-200 px-2.5 py-0.5 rounded-full text-xs font-bold tracking-wide shrink-0">Step 3</span>
      <h1 class="m-0 text-xl font-semibold flex-1">Export Schema Mapper</h1>
      <button
        class="px-3 py-1 border border-slate-300 rounded-md bg-white text-sm text-slate-500 cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed hover:enabled:bg-slate-50"
        :disabled="loading"
        @click="load"
      >Refresh</button>
    </div>

    <p class="text-slate-500 text-sm mt-2 mb-6 leading-relaxed">
      Select a source table, choose which columns to include and rename them for the target system
      (e.g. ServiceNow), then add joins from related tables.
      Changes are saved before the export runs.
    </p>

    <p v-if="loading" class="text-slate-500">Loading…</p>
    <p v-else-if="error" class="text-red-600">{{ error }}</p>

    <template v-else-if="sourceSchema">
      <!-- Presets toolbar -->
      <PresetsToolbar :can-save="!!selectedTable" :get-config="buildMappingConfig" @apply="onApplyPreset" />

      <!-- Primary table selector -->
      <div class="mb-7">
        <h2 class="text-base font-semibold text-slate-900 mb-2.5">Primary Source Table</h2>
        <div class="flex items-center gap-3 flex-wrap">
          <select
            class="table-select px-2.5 py-2 border border-slate-300 rounded-md text-sm text-slate-900 bg-white cursor-pointer min-w-56"
            v-model="selectedTable"
          >
            <option value="" disabled>— select a table —</option>
            <option v-for="t in sourceSchema.tables" :key="t.name" :value="t.name">
              {{ t.name }} ({{ t.columns.length }} cols)
            </option>
          </select>
          <span class="conn-chip text-xs text-slate-500 bg-slate-100 border border-slate-200 px-2.5 py-1 rounded-full">{{ sourceSchema.connectionLabel }}</span>
        </div>
      </div>

      <!-- Column mapping -->
      <ColumnMappingTable
        v-if="selectedTable"
        :fields="fields"
        :column-map="selectedTableColumnMap"
        @dirty="markDirty"
      />

      <!-- Suggested relations (detected from foreign keys) -->
      <SuggestedRelations
        v-if="selectedTable"
        :suggestions="suggestedRelations"
        :selected-table-name="selectedTable"
        @add="addSuggestedRelation"
      />

      <!-- Relations -->
      <RelationsSection
        v-if="selectedTable"
        :relations="relations"
        :relatable-tables="relatableTables"
        :selected-table-columns="selectedTableColumns"
        @add="addRelation"
        @remove="removeRelation"
        @dirty="markDirty"
      />

      <!-- Format selection -->
      <ExportFormatPicker
        v-if="selectedTable"
        :model-value="selectedFormat"
        @update:model-value="setFormat"
      />

      <!-- Nested JSON Structure (JSON export only) -->
      <NestedGroupsSection
        v-if="selectedTable && selectedFormat === 'json'"
        :groups="nestedGroups"
        :available-tables="sourceSchema.tables"
        @add="addNestedGroup"
        @remove="removeNestedGroup"
        @dirty="markDirty"
      />

      <!-- JSON envelope / root wrapper (JSON export only) -->
      <JsonEnvelopeEditor
        v-if="selectedTable && selectedFormat === 'json'"
        v-model="jsonWrapper"
        @dirty="markDirty"
      />

      <!-- Save status -->
      <div v-if="saveError" class="save-error px-3.5 py-2.5 bg-red-50 border border-red-200 rounded-md text-sm text-red-600 mb-4">{{ saveError }}</div>
      <div v-if="saved && !saveError" class="save-ok px-3.5 py-2.5 bg-green-50 border border-green-200 rounded-md text-sm text-green-700 mb-4">Mapping saved.</div>

      <!-- Navigation -->
      <div class="flex items-center justify-between mt-6 gap-3">
        <button class="px-4 py-2 border border-slate-300 rounded-md bg-white text-slate-500 text-sm cursor-pointer hover:bg-slate-50" @click="router.push({ name: 'source-schema' })">← Back to Source Schema</button>
        <div v-if="selectedTable" class="flex gap-2.5">
          <button class="btn-save px-4 py-2 border border-slate-900 rounded-md bg-white text-slate-900 text-sm font-semibold cursor-pointer hover:enabled:bg-slate-50 disabled:opacity-50 disabled:cursor-not-allowed" :disabled="saving" @click="saveMapping">{{ saving ? 'Saving…' : 'Save Mapping' }}</button>
          <button class="px-5 py-2 border-0 rounded-md bg-slate-900 text-slate-200 text-sm font-semibold cursor-pointer hover:enabled:bg-slate-800 disabled:opacity-50 disabled:cursor-not-allowed" :disabled="saving" @click="proceed">Save & Go to Export →</button>
        </div>
      </div>
    </template>
  </div>
</template>
