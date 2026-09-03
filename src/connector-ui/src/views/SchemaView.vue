<script setup lang="ts">
import { ref, computed, watch, onMounted, nextTick } from 'vue'
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
} from '@/api/mapping'
import { getPreview, type PreviewResult } from '@/api/pipeline'
import PresetsToolbar from '@/components/PresetsToolbar.vue'
import ExportFormatPicker from '@/components/ExportFormatPicker.vue'
import ColumnMappingTable from '@/components/ColumnMappingTable.vue'
import RelatedJoinsPanel from '@/components/RelatedJoinsPanel.vue'
import JsonExportOptionsPanel from '@/components/JsonExportOptionsPanel.vue'
import { type SuggestedRelation } from '@/components/SuggestedRelations.vue'
import { findSuggestedRelations } from '@/lib/suggestedRelations'
import PreviewTable from '@/components/PreviewTable.vue'

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

// True when a config already carries JSON-only mapping options — used to default the
// preview toggle so an existing nested-JSON mapping opens showing those options.
function hasJsonOptions(cfg: { nestedGroups?: MappingNestedGroup[]; jsonWrapper: ExportJsonWrapperConfig | null }) {
  return (cfg.nestedGroups ?? []).length > 0 || cfg.jsonWrapper !== null
}

function onApplyPreset(cfg: ExportMappingConfig) {
  fields.value = cfg.fields.map((f) => ({ ...f }))
  relations.value = cfg.relations.map(cloneRelation)
  nestedGroups.value = (cfg.nestedGroups ?? []).map(cloneNestedGroup)
  jsonWrapper.value = cloneWrapper(cfg.jsonWrapper)
  if (hasJsonOptions(cfg)) previewFormat.value = 'json'
  snapshotToCache(cfg.sourceTable)
  selectedTable.value = cfg.sourceTable
  saved.value = false
  dirty.value = true
}

// ── Format preview toggle ────────────────────────────────────────────────────
// Purely local to this editor — picks which format-specific mapping options to show
// (the JSON-only nested-group/envelope sections below). This does NOT set the actual
// export format: that's chosen independently per run (Export view) or for the
// schedule (Settings), each of which reads the saved mapping regardless of this toggle.
const previewFormat = ref<'xlsx' | 'csv' | 'json'>('json')

function setPreviewFormat(fmt: 'xlsx' | 'csv' | 'json') {
  previewFormat.value = fmt
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

function primaryKeyColumns(t: SourceTable): string[] {
  return t.columns.filter((c) => c.primaryKey).map((c) => c.name)
}

// Shown per-option in the primary-table picker, so PK status (including composite keys)
// is visible before selecting — previously only visible by expanding a table's column list.
function tablePkLabel(t: SourceTable): string {
  const pk = primaryKeyColumns(t)
  if (pk.length === 0) return 'no PK'
  if (pk.length === 1) return `PK: ${pk[0]}`
  return `composite PK: ${pk.join(', ')}`
}

const selectedTablePkColumns = computed<string[]>(() => {
  const t = sourceSchema.value?.tables.find((t) => t.name === selectedTable.value)
  return t ? primaryKeyColumns(t) : []
})

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

// Set around the programmatic selectedTable assignment in load() so restoring an already-saved
// mapping doesn't mark the page dirty — without this, simply opening Step 3 wrongly triggers the
// "unsaved changes" leave-confirmation, and the live preview below always looked stale.
let restoringSavedTable = false

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
  if (restoringSavedTable) return
  saved.value = false
  dirty.value = true
})

// ── Suggested relations (from FK metadata) ─────────────────────────────────────
const suggestedRelations = computed<SuggestedRelation[]>(() =>
  findSuggestedRelations(sourceSchema.value, selectedTable.value, relations.value),
)

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

// For JSON export, DynamicExportService builds nested output from Fields + NestedGroups only —
// Relations are silently absent from that path (they only flatten into the plain/flat query).
// Surfaced here so a relation someone already configured doesn't quietly vanish from the file
// the moment they add a nested group or a custom envelope.
const relationsDroppedForJson = computed(
  () =>
    previewFormat.value === 'json' &&
    relations.value.some((r) => r.enabled) &&
    (nestedGroups.value.length > 0 || jsonWrapper.value !== null),
)

// ── Live preview ─────────────────────────────────────────────────────────────────
// Reuses the same /api/pipeline/preview endpoint the Export step uses (real Postgres data,
// same nested-vs-flat decision DynamicExportService makes for the real export) so what shows
// here is exactly what the export will contain — no separate frontend re-implementation to
// keep in sync. Reflects the last *saved* mapping, refreshed after every successful save.
const preview = ref<PreviewResult | null>(null)
const previewLoading = ref(false)
const previewError = ref<string | null>(null)

async function loadPreview() {
  previewLoading.value = true
  previewError.value = null
  try {
    preview.value = await getPreview()
    if (!preview.value) previewError.value = 'Preview endpoint returned no data.'
  } catch {
    previewError.value = 'Could not reach the API. Is the backend service running?'
  } finally {
    previewLoading.value = false
  }
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
  await loadPreview()
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

    sourceSchema.value = schema

    if (existingMapping) {
      fields.value = existingMapping.fields.map((f) => ({ ...f }))
      relations.value = existingMapping.relations.map(cloneRelation)
      nestedGroups.value = (existingMapping.nestedGroups ?? []).map(cloneNestedGroup)
      jsonWrapper.value = cloneWrapper(existingMapping.jsonWrapper)
      if (hasJsonOptions(existingMapping)) previewFormat.value = 'json'
      // Seed the cache so switching away and back restores the saved state.
      snapshotToCache(existingMapping.sourceTable)
      restoringSavedTable = true
      selectedTable.value = existingMapping.sourceTable
      await nextTick()
      restoringSavedTable = false
      loadPreview()
    }
  } catch (err) {
    error.value = err instanceof Error ? err.message : 'Could not reach the API. Is the backend service running?'
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
              {{ t.name }} ({{ t.columns.length }} cols — {{ tablePkLabel(t) }})
            </option>
          </select>
          <span class="conn-chip text-xs text-slate-500 bg-slate-100 border border-slate-200 px-2.5 py-1 rounded-full">{{ sourceSchema.connectionLabel }}</span>
        </div>
        <p v-if="selectedTable && selectedTablePkColumns.length === 0"
          class="text-xs text-amber-900 bg-amber-50 border border-amber-200 rounded-md px-3 py-2 mt-2 leading-relaxed">
          <strong>"{{ selectedTable }}" has no primary key.</strong>
          Row identity, relation joins, and the suggested join key default may be unreliable — verify manually.
        </p>
      </div>

      <!-- Everything below depends on a primary table being selected first. -->
      <template v-if="selectedTable">
        <!-- Column mapping -->
        <ColumnMappingTable
          :fields="fields"
          :column-map="selectedTableColumnMap"
          @dirty="markDirty"
        />

        <!-- Relations (optional) -->
        <RelatedJoinsPanel
          :relations="relations"
          :relatable-tables="relatableTables"
          :selected-table-columns="selectedTableColumns"
          :selected-table-name="selectedTable"
          :suggestions="suggestedRelations"
          @add-suggested="addSuggestedRelation"
          @add="addRelation"
          @remove="removeRelation"
          @dirty="markDirty"
        />

        <!-- Format preview toggle: which format-specific options to show below.
             The actual export format is chosen per run (Export view) or for the
             schedule (Settings) — not here. -->
        <ExportFormatPicker :model-value="previewFormat" @update:model-value="setPreviewFormat" />

        <!-- Silent data-loss warning: relations don't carry over into nested JSON output. -->
        <div
          v-if="relationsDroppedForJson"
          class="text-xs text-amber-900 bg-amber-50 border border-amber-200 rounded-md px-3 py-2 mb-7 leading-relaxed"
        >
          <strong>Heads up:</strong> for JSON export, Related Table Joins are ignored once Nested JSON Structure
          or a custom envelope is used — that data won't appear in the file. Pull it in as a <strong>Nested Group</strong> instead.
        </div>

        <!-- JSON-only options (optional) -->
        <JsonExportOptionsPanel
          v-if="previewFormat === 'json'"
          :nested-groups="nestedGroups"
          :available-tables="sourceSchema.tables"
          v-model:json-wrapper="jsonWrapper"
          @add="addNestedGroup"
          @remove="removeNestedGroup"
          @dirty="markDirty"
        />

        <!-- Live preview of the last saved mapping — same query the real export runs. -->
        <div class="mb-2">
          <p v-if="dirty && preview" class="text-xs text-amber-700 mt-0 mb-2">
            Showing the last saved mapping — Save Mapping to preview your latest edits.
          </p>
          <PreviewTable :preview="preview" :loading="previewLoading" :error="previewError" @refresh="loadPreview" />
        </div>
      </template>

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
