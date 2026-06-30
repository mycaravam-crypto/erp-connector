<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import { useRouter, onBeforeRouteLeave } from 'vue-router'
import { getSourceSchema, type SourceTable, type SourceColumn } from '@/api/connection'
import {
  getExportMapping,
  saveExportMapping,
  getPresets,
  savePreset,
  deletePreset,
  type MappingField,
  type MappingRelation,
  type ExportMappingConfig,
} from '@/api/erp'

const router = useRouter()

// ── Source schema ──────────────────────────────────────────────────────────────
const sourceSchema = ref<{ connectionLabel: string; tables: SourceTable[] } | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)

// ── Mapping state ──────────────────────────────────────────────────────────────
const selectedTable = ref('')
const fields = ref<MappingField[]>([])
const relations = ref<MappingRelation[]>([])
const saving = ref(false)
const saveError = ref<string | null>(null)
const saved = ref(false)
const dirty = ref(false)

// ── Presets ────────────────────────────────────────────────────────────────────
const presets = ref<Record<string, ExportMappingConfig>>({})
const selectedPreset = ref('')
const presetSaving = ref(false)
const presetError = ref<string | null>(null)

// Inline save-as state
const showSaveInput = ref(false)
const saveInputName = ref('')

// Inline delete-confirm state
const confirmingDelete = ref(false)

const presetNames = computed(() => Object.keys(presets.value).sort())

async function loadPresets() {
  try {
    presets.value = await getPresets()
  } catch {
    presetError.value = 'Could not load presets. Is the backend service running?'
  }
}

function applyPreset(name: string) {
  const cfg = presets.value[name]
  if (!cfg) return
  fields.value = cfg.fields.map((f) => ({ ...f }))
  relations.value = cfg.relations.map((r) => ({ ...r, strategyOptions: { ...r.strategyOptions } }))
  snapshotToCache(cfg.sourceTable)
  selectedTable.value = cfg.sourceTable
  saved.value = false
  dirty.value = true
  presetError.value = null
}

function openSaveInput() {
  if (!selectedTable.value) {
    presetError.value = 'Select a source table before saving a preset.'
    return
  }
  saveInputName.value = selectedPreset.value || ''
  showSaveInput.value = true
  presetError.value = null
}

async function confirmSavePreset() {
  const name = saveInputName.value.trim()
  if (!name) {
    presetError.value = 'Preset name cannot be empty.'
    return
  }

  const config: ExportMappingConfig = {
    sourceTable: selectedTable.value,
    fields: fields.value.map((f) => ({
      sourceName: f.sourceName,
      targetName: f.targetName.trim() || f.sourceName,
      enabled: f.enabled,
    })),
    relations: relations.value,
  }

  presetSaving.value = true
  presetError.value = null
  const result = await savePreset(name, config)
  presetSaving.value = false

  if (!result.ok) {
    presetError.value = result.error ?? 'Failed to save preset.'
    return
  }
  await loadPresets()
  selectedPreset.value = name
  showSaveInput.value = false
}

async function confirmDeletePreset() {
  if (!selectedPreset.value) return

  presetSaving.value = true
  presetError.value = null
  const result = await deletePreset(selectedPreset.value)
  presetSaving.value = false

  if (!result.ok) {
    presetError.value = result.error ?? 'Failed to delete preset.'
    return
  }
  selectedPreset.value = ''
  confirmingDelete.value = false
  await loadPresets()
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

const enabledFieldCount = computed(() => fields.value.filter((f) => f.enabled).length)

function getTableColumns(tableName: string): SourceColumn[] {
  return sourceSchema.value?.tables.find((t) => t.name === tableName)?.columns ?? []
}

// ── Per-table state cache ──────────────────────────────────────────────────────
// Keeps field/relation edits alive when the user switches between tables.
const tableCache = new Map<string, { fields: MappingField[]; relations: MappingRelation[] }>()

function snapshotToCache(tableName: string) {
  if (!tableName) return
  tableCache.set(tableName, {
    fields: fields.value.map((f) => ({ ...f })),
    relations: relations.value.map((r) => ({ ...r, strategyOptions: { ...r.strategyOptions } })),
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
    relations.value = cached.relations.map((r) => ({ ...r, strategyOptions: { ...r.strategyOptions } }))
  } else {
    const cols = sourceSchema.value?.tables.find((t) => t.name === newTable)?.columns ?? []
    fields.value = cols.map((col) => ({
      sourceName: col.name,
      targetName: col.name,
      enabled: col.primaryKey,
    }))
    relations.value = []
  }
  saved.value = false
  dirty.value = true
})

// ── Relation management ────────────────────────────────────────────────────────
function addRelation() {
  relations.value.push({
    relatedTable: '',
    joinKey: '',
    sourceJoinKey: sourcePkColumn.value,
    targetField: '',
    enabled: true,
    flattenStrategy: 'string_join',
    strategyOptions: { sourceField: '', delimiter: ', ' },
  })
  saved.value = false
  dirty.value = true
}

function removeRelation(idx: number) {
  relations.value.splice(idx, 1)
  saved.value = false
  dirty.value = true
}

// ── Bulk column selection ──────────────────────────────────────────────────────
function selectAllFields() {
  fields.value.forEach((f) => { f.enabled = true })
  saved.value = false
  dirty.value = true
}

function deselectAllFields() {
  fields.value.forEach((f) => { f.enabled = false })
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

  const config: ExportMappingConfig = {
    sourceTable: selectedTable.value,
    fields: fields.value.map((f) => ({
      sourceName: f.sourceName,
      targetName: f.targetName.trim() || f.sourceName,
      enabled: f.enabled,
    })),
    relations: relations.value,
  }

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
      relations.value = existingMapping.relations.map((r) => ({
        ...r,
        strategyOptions: { ...r.strategyOptions },
      }))
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

onMounted(() => { load(); loadPresets() })
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
      <div class="mb-5">
        <div class="flex items-center gap-2 flex-wrap">
          <h2 class="text-base font-semibold text-slate-900 shrink-0">Presets</h2>
          <select
            class="preset-select flex-1 min-w-48 px-2.5 py-2 border border-slate-300 rounded-md text-sm text-slate-900 bg-white cursor-pointer"
            v-model="selectedPreset"
          >
            <option value="" disabled>— select a preset —</option>
            <option v-for="name in presetNames" :key="name" :value="name">{{ name }}</option>
          </select>
          <button
            class="load-btn px-3 py-2 border border-slate-300 rounded-md bg-white text-sm text-slate-700 cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed hover:enabled:bg-slate-50"
            :disabled="!selectedPreset || presetSaving"
            @click="applyPreset(selectedPreset)"
          >Load</button>
          <button
            class="save-as-btn px-3 py-2 border border-slate-300 rounded-md bg-white text-sm text-slate-700 cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed hover:enabled:bg-slate-50"
            :disabled="presetSaving"
            @click="openSaveInput"
          >Save As…</button>
          <button
            class="delete-preset-btn px-3 py-2 border border-red-200 rounded-md bg-white text-sm text-red-600 cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed hover:enabled:bg-red-50"
            :disabled="!selectedPreset || presetSaving"
            @click="confirmingDelete = true"
          >Delete</button>
        </div>

        <!-- Inline save-as input -->
        <div v-if="showSaveInput" class="flex items-center gap-2 mt-2 flex-wrap">
          <input
            class="px-2.5 py-1.5 border border-slate-300 rounded-md text-sm text-slate-900 bg-white outline-none focus:border-slate-900 min-w-48 flex-1"
            type="text"
            placeholder="Preset name…"
            v-model="saveInputName"
            @keyup.enter="confirmSavePreset"
            @keyup.esc="showSaveInput = false"
          />
          <button
            class="px-3 py-1.5 border-0 rounded-md bg-slate-900 text-slate-200 text-sm font-semibold cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed hover:enabled:bg-slate-800"
            :disabled="presetSaving || !saveInputName.trim()"
            @click="confirmSavePreset"
          >{{ presetSaving ? 'Saving…' : 'Save' }}</button>
          <button
            class="px-3 py-1.5 border border-slate-300 rounded-md bg-white text-sm text-slate-500 cursor-pointer hover:bg-slate-50"
            @click="showSaveInput = false"
          >Cancel</button>
        </div>

        <!-- Inline delete confirm -->
        <div v-if="confirmingDelete" class="flex items-center gap-2 mt-2 text-sm">
          <span class="text-red-700 font-semibold">Delete "{{ selectedPreset }}"?</span>
          <button
            class="px-3 py-1 border border-red-300 rounded-md bg-red-50 text-red-700 text-sm font-semibold cursor-pointer disabled:opacity-50 hover:bg-red-100"
            :disabled="presetSaving"
            @click="confirmDeletePreset"
          >{{ presetSaving ? 'Deleting…' : 'Yes, delete' }}</button>
          <button
            class="px-3 py-1 border border-slate-300 rounded-md bg-white text-sm text-slate-500 cursor-pointer hover:bg-slate-50"
            @click="confirmingDelete = false"
          >Cancel</button>
        </div>
      </div>
      <div v-if="presetError" class="preset-error px-3.5 py-2.5 bg-red-50 border border-red-200 rounded-md text-sm text-red-600 mb-5">{{ presetError }}</div>

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
      <div v-if="selectedTable" class="mb-7">
        <div class="flex items-center gap-2 mb-2.5 flex-wrap">
          <h2 class="text-base font-semibold text-slate-900 m-0">Columns</h2>
          <span class="text-xs font-medium text-slate-500 bg-slate-100 px-2 py-0.5 rounded-full">{{ enabledFieldCount }} / {{ fields.length }} selected</span>
          <div class="ml-auto flex gap-1.5">
            <button class="px-2.5 py-1 border border-slate-300 rounded text-xs text-slate-600 bg-white cursor-pointer hover:bg-slate-50" @click="selectAllFields">Select All</button>
            <button class="px-2.5 py-1 border border-slate-300 rounded text-xs text-slate-600 bg-white cursor-pointer hover:bg-slate-50" @click="deselectAllFields">Deselect All</button>
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
                <input type="checkbox" v-model="field.enabled" @change="saved = false; dirty = true" />
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
                  @input="saved = false; dirty = true"
                />
              </td>
              <td class="px-2.5 py-2 border-b border-slate-200 align-middle">
                <span class="inline-block px-1.5 py-0.5 bg-slate-100 border border-slate-200 rounded text-xs text-slate-500 whitespace-nowrap">
                  {{ selectedTableColumnMap[field.sourceName]?.type ?? '' }}
                </span>
              </td>
              <td class="px-2.5 py-2 border-b border-slate-200 align-middle">
                <span v-if="selectedTableColumnMap[field.sourceName]?.primaryKey" class="pk-badge text-[0.65rem] font-bold bg-blue-100 text-blue-800 px-1.5 py-0.5 rounded">PK</span>
                <span v-else class="text-slate-200">—</span>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <!-- Relations -->
      <div v-if="selectedTable" class="mb-7">
        <div class="flex items-center justify-between mb-2">
          <h2 class="text-base font-semibold text-slate-900 m-0">Related Table Joins</h2>
          <button
            class="add-btn px-3 py-1.5 border border-slate-300 rounded-md bg-white text-sm text-slate-500 cursor-pointer whitespace-nowrap hover:bg-slate-50"
            @click="addRelation"
          >+ Add Relation</button>
        </div>
        <p class="text-sm text-slate-500 mb-3 leading-snug">
          Add 1:N joins to pull aggregated values from related tables into the export row.
          Use <em>String Join</em> to concatenate values, or <em>Array</em> to comma-separate them.
        </p>

        <div v-if="relations.length === 0" class="text-sm text-slate-400 px-4 py-3 border border-dashed border-slate-300 rounded-md text-center">
          No relations configured.
        </div>

        <div
          v-for="(rel, idx) in relations"
          :key="idx"
          :class="['relation-card flex gap-3 items-start px-4 py-3 border rounded-lg mb-2 bg-white', rel.enabled ? 'border-blue-200 bg-sky-50' : 'border-slate-200 opacity-65']"
        >
          <div class="pt-1 shrink-0">
            <input type="checkbox" v-model="rel.enabled" class="cursor-pointer w-4 h-4" @change="saved = false; dirty = true" />
          </div>

          <div class="flex-1 flex flex-col gap-2">
            <div class="flex gap-2.5 flex-wrap">
              <div class="flex flex-col gap-1 flex-1 min-w-36">
                <label class="text-[0.7rem] font-semibold text-slate-500 uppercase tracking-wide whitespace-nowrap">Related Table</label>
                <select v-model="rel.relatedTable" class="px-2 py-1.5 border border-slate-300 rounded text-sm text-slate-900 bg-white w-full" @change="saved = false; dirty = true">
                  <option value="" disabled>— select —</option>
                  <option v-for="t in sourceSchema.tables.filter((t) => t.name !== selectedTable)" :key="t.name" :value="t.name">{{ t.name }}</option>
                </select>
              </div>
              <div class="flex flex-col gap-1 flex-1 min-w-36">
                <label class="text-[0.7rem] font-semibold text-slate-500 uppercase tracking-wide whitespace-nowrap">Source Column</label>
                <select v-model="rel.sourceJoinKey" class="px-2 py-1.5 border border-slate-300 rounded text-sm text-slate-900 bg-white w-full" @change="saved = false; dirty = true">
                  <option value="" disabled>— select —</option>
                  <option v-for="c in selectedTableColumns" :key="c.name" :value="c.name">{{ c.name }}{{ c.primaryKey ? ' (PK)' : '' }}</option>
                </select>
              </div>
              <div class="flex flex-col gap-1 flex-1 min-w-36">
                <label class="text-[0.7rem] font-semibold text-slate-500 uppercase tracking-wide whitespace-nowrap">Join Column (in {{ rel.relatedTable || '…' }})</label>
                <select v-model="rel.joinKey" :disabled="!rel.relatedTable" class="px-2 py-1.5 border border-slate-300 rounded text-sm text-slate-900 bg-white w-full disabled:bg-slate-50 disabled:text-slate-400" @change="saved = false; dirty = true">
                  <option value="" disabled>— select —</option>
                  <option v-for="c in getTableColumns(rel.relatedTable)" :key="c.name" :value="c.name">{{ c.name }}</option>
                </select>
              </div>
              <div class="flex flex-col gap-1 flex-1 min-w-36">
                <label class="text-[0.7rem] font-semibold text-slate-500 uppercase tracking-wide whitespace-nowrap">Target Field Name</label>
                <input class="px-2 py-1.5 border border-slate-300 rounded text-sm text-slate-900 bg-white w-full outline-none focus:border-slate-900" type="text" placeholder="e.g. maintenance_states" v-model="rel.targetField" @input="saved = false; dirty = true" />
              </div>
            </div>

            <div class="flex gap-2.5 flex-wrap">
              <div class="flex flex-col gap-1 flex-1 min-w-36">
                <label class="text-[0.7rem] font-semibold text-slate-500 uppercase tracking-wide whitespace-nowrap">Value Column (from {{ rel.relatedTable || '…' }})</label>
                <select v-model="rel.strategyOptions.sourceField" :disabled="!rel.relatedTable" class="px-2 py-1.5 border border-slate-300 rounded text-sm text-slate-900 bg-white w-full disabled:bg-slate-50 disabled:text-slate-400" @change="saved = false; dirty = true">
                  <option value="" disabled>— select —</option>
                  <option v-for="c in getTableColumns(rel.relatedTable)" :key="c.name" :value="c.name">{{ c.name }}</option>
                </select>
              </div>
              <div class="flex flex-col gap-1 flex-1 min-w-36">
                <label class="text-[0.7rem] font-semibold text-slate-500 uppercase tracking-wide whitespace-nowrap">Flatten Strategy</label>
                <select v-model="rel.flattenStrategy" class="px-2 py-1.5 border border-slate-300 rounded text-sm text-slate-900 bg-white w-full" @change="saved = false; dirty = true">
                  <option value="string_join">String Join (concatenate with delimiter)</option>
                  <option value="array">Array (comma-separated list)</option>
                </select>
              </div>
              <div v-if="rel.flattenStrategy === 'string_join'" class="flex flex-col gap-1 w-24 shrink-0">
                <label class="text-[0.7rem] font-semibold text-slate-500 uppercase tracking-wide whitespace-nowrap">Delimiter</label>
                <input class="px-2 py-1.5 border border-slate-300 rounded text-sm text-slate-900 bg-white w-full outline-none focus:border-slate-900" type="text" v-model="rel.strategyOptions.delimiter" placeholder=", " @input="saved = false; dirty = true" />
              </div>
            </div>
          </div>

          <button
            class="rel-remove-btn shrink-0 px-2 py-1 border border-red-200 rounded text-red-600 bg-white text-base leading-none cursor-pointer hover:bg-red-50"
            @click="removeRelation(idx)"
            title="Remove relation"
          >×</button>
        </div>
      </div>

      <!-- Format selection -->
      <div v-if="selectedTable" class="mb-7">
        <h2 class="text-base font-semibold text-slate-900 mb-2.5">Export Format</h2>
        <div class="flex gap-3">
          <button
            v-for="fmt in [
              { id: 'xlsx', label: 'Excel (.xlsx)', desc: 'Full format with metadata row — required for ServiceNow Transform Map' },
              { id: 'csv',  label: 'CSV (.csv)',   desc: 'Plain text, comma-separated — compatible with most tools' },
              { id: 'json', label: 'JSON (.json)', desc: 'Machine-readable — useful for APIs and custom pipelines' },
            ]"
            :key="fmt.id"
            :class="['format-btn flex-1 flex flex-col gap-1 px-4 py-3 border-2 rounded-lg bg-white cursor-pointer text-left transition-colors', selectedFormat === fmt.id ? 'border-slate-900 bg-slate-50' : 'border-slate-200 hover:border-slate-400']"
            @click="setFormat(fmt.id as 'xlsx' | 'csv' | 'json')"
          >
            <span class="text-sm font-semibold text-slate-900">{{ fmt.label }}</span>
            <span class="text-xs text-slate-500 leading-snug">{{ fmt.desc }}</span>
          </button>
        </div>
      </div>

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
