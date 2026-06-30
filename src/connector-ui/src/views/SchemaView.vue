<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { getSourceSchema, type SourceTable, type SourceColumn } from '@/api/connection'
import {
  getExportMapping,
  saveExportMapping,
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

// ── Table selection ────────────────────────────────────────────────────────────
function onTableSelect(tableName: string) {
  selectedTable.value = tableName
  const cols = sourceSchema.value?.tables.find((t) => t.name === tableName)?.columns ?? []
  fields.value = cols.map((col) => ({
    sourceName: col.name,
    targetName: col.name,
    enabled: col.primaryKey,
  }))
  relations.value = []
  saved.value = false
}

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
}

function removeRelation(idx: number) {
  relations.value.splice(idx, 1)
  saved.value = false
}

// ── Save ───────────────────────────────────────────────────────────────────────
async function saveMapping(): Promise<boolean> {
  saveError.value = null
  saved.value = false

  if (!selectedTable.value) {
    saveError.value = 'Please select a source table.'
    return false
  }

  const badFields = fields.value.filter((f) => f.enabled && !f.targetName.trim())
  if (badFields.length > 0) {
    saveError.value = `Set target names for: ${badFields.map((f) => f.sourceName).join(', ')}`
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
      selectedTable.value = existingMapping.sourceTable
      fields.value = existingMapping.fields.map((f) => ({ ...f }))
      relations.value = existingMapping.relations.map((r) => ({
        ...r,
        strategyOptions: { ...r.strategyOptions },
      }))
    }
  } catch {
    error.value = 'Could not reach the API. Is the backend running on :5189?'
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
      <!-- Primary table selector -->
      <div class="mb-7">
        <h2 class="text-base font-semibold text-slate-900 mb-2.5">Primary Source Table</h2>
        <div class="flex items-center gap-3 flex-wrap">
          <select
            class="table-select px-2.5 py-2 border border-slate-300 rounded-md text-sm text-slate-900 bg-white cursor-pointer min-w-56"
            :value="selectedTable"
            @change="onTableSelect(($event.target as HTMLSelectElement).value)"
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
        <h2 class="text-base font-semibold text-slate-900 mb-2.5 flex items-center gap-2">
          Columns
          <span class="text-xs font-medium text-slate-500 bg-slate-100 px-2 py-0.5 rounded-full">{{ enabledFieldCount }} / {{ fields.length }} selected</span>
        </h2>
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
                <input type="checkbox" v-model="field.enabled" @change="saved = false" />
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
                  @input="saved = false"
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
            <input type="checkbox" v-model="rel.enabled" class="cursor-pointer w-4 h-4" @change="saved = false" />
          </div>

          <div class="flex-1 flex flex-col gap-2">
            <div class="flex gap-2.5 flex-wrap">
              <div class="flex flex-col gap-1 flex-1 min-w-36">
                <label class="text-[0.7rem] font-semibold text-slate-500 uppercase tracking-wide whitespace-nowrap">Related Table</label>
                <select v-model="rel.relatedTable" class="px-2 py-1.5 border border-slate-300 rounded text-sm text-slate-900 bg-white w-full" @change="saved = false">
                  <option value="" disabled>— select —</option>
                  <option v-for="t in sourceSchema.tables.filter((t) => t.name !== selectedTable)" :key="t.name" :value="t.name">{{ t.name }}</option>
                </select>
              </div>
              <div class="flex flex-col gap-1 flex-1 min-w-36">
                <label class="text-[0.7rem] font-semibold text-slate-500 uppercase tracking-wide whitespace-nowrap">Source Column</label>
                <select v-model="rel.sourceJoinKey" class="px-2 py-1.5 border border-slate-300 rounded text-sm text-slate-900 bg-white w-full" @change="saved = false">
                  <option value="" disabled>— select —</option>
                  <option v-for="c in selectedTableColumns" :key="c.name" :value="c.name">{{ c.name }}{{ c.primaryKey ? ' (PK)' : '' }}</option>
                </select>
              </div>
              <div class="flex flex-col gap-1 flex-1 min-w-36">
                <label class="text-[0.7rem] font-semibold text-slate-500 uppercase tracking-wide whitespace-nowrap">Join Column (in {{ rel.relatedTable || '…' }})</label>
                <select v-model="rel.joinKey" :disabled="!rel.relatedTable" class="px-2 py-1.5 border border-slate-300 rounded text-sm text-slate-900 bg-white w-full disabled:bg-slate-50 disabled:text-slate-400" @change="saved = false">
                  <option value="" disabled>— select —</option>
                  <option v-for="c in getTableColumns(rel.relatedTable)" :key="c.name" :value="c.name">{{ c.name }}</option>
                </select>
              </div>
              <div class="flex flex-col gap-1 flex-1 min-w-36">
                <label class="text-[0.7rem] font-semibold text-slate-500 uppercase tracking-wide whitespace-nowrap">Target Field Name</label>
                <input class="px-2 py-1.5 border border-slate-300 rounded text-sm text-slate-900 bg-white w-full outline-none focus:border-slate-900" type="text" placeholder="e.g. maintenance_states" v-model="rel.targetField" @input="saved = false" />
              </div>
            </div>

            <div class="flex gap-2.5 flex-wrap">
              <div class="flex flex-col gap-1 flex-1 min-w-36">
                <label class="text-[0.7rem] font-semibold text-slate-500 uppercase tracking-wide whitespace-nowrap">Value Column (from {{ rel.relatedTable || '…' }})</label>
                <select v-model="rel.strategyOptions.sourceField" :disabled="!rel.relatedTable" class="px-2 py-1.5 border border-slate-300 rounded text-sm text-slate-900 bg-white w-full disabled:bg-slate-50 disabled:text-slate-400" @change="saved = false">
                  <option value="" disabled>— select —</option>
                  <option v-for="c in getTableColumns(rel.relatedTable)" :key="c.name" :value="c.name">{{ c.name }}</option>
                </select>
              </div>
              <div class="flex flex-col gap-1 flex-1 min-w-36">
                <label class="text-[0.7rem] font-semibold text-slate-500 uppercase tracking-wide whitespace-nowrap">Flatten Strategy</label>
                <select v-model="rel.flattenStrategy" class="px-2 py-1.5 border border-slate-300 rounded text-sm text-slate-900 bg-white w-full" @change="saved = false">
                  <option value="string_join">String Join (concatenate with delimiter)</option>
                  <option value="array">Array (comma-separated list)</option>
                </select>
              </div>
              <div v-if="rel.flattenStrategy === 'string_join'" class="flex flex-col gap-1 w-24 shrink-0">
                <label class="text-[0.7rem] font-semibold text-slate-500 uppercase tracking-wide whitespace-nowrap">Delimiter</label>
                <input class="px-2 py-1.5 border border-slate-300 rounded text-sm text-slate-900 bg-white w-full outline-none focus:border-slate-900" type="text" v-model="rel.strategyOptions.delimiter" placeholder=", " @input="saved = false" />
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
          <button class="px-5 py-2 border-0 rounded-md bg-slate-900 text-slate-200 text-sm font-semibold cursor-pointer hover:enabled:bg-slate-800 disabled:opacity-50 disabled:cursor-not-allowed" :disabled="saving" @click="proceed">Run Export ({{ selectedFormat.toUpperCase() }}) →</button>
        </div>
      </div>
    </template>
  </div>
</template>
