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
  // Initialise fields: enable only the PK column by default; pre-fill target names.
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
  <div class="page">
    <div class="step-header">
      <span class="step-badge">Step 3</span>
      <h1>Export Schema Mapper</h1>
      <button class="refresh-btn" :disabled="loading" @click="load">Refresh</button>
    </div>

    <p class="intro">
      Select a source table, choose which columns to include and rename them for the target system
      (e.g. ServiceNow), then add joins from related tables.
      Changes are saved before the export runs.
    </p>

    <p v-if="loading" class="info">Loading…</p>
    <p v-else-if="error" class="error">{{ error }}</p>

    <template v-else-if="sourceSchema">
      <!-- Primary table selector -->
      <div class="section">
        <h2>Primary Source Table</h2>
        <div class="table-select-row">
          <select
            class="table-select"
            :value="selectedTable"
            @change="onTableSelect(($event.target as HTMLSelectElement).value)"
          >
            <option value="" disabled>— select a table —</option>
            <option v-for="t in sourceSchema.tables" :key="t.name" :value="t.name">
              {{ t.name }} ({{ t.columns.length }} cols)
            </option>
          </select>
          <span class="conn-chip">{{ sourceSchema.connectionLabel }}</span>
        </div>
      </div>

      <!-- Column mapping -->
      <div v-if="selectedTable" class="section">
        <h2>
          Columns
          <span class="section-meta">{{ enabledFieldCount }} / {{ fields.length }} selected</span>
        </h2>
        <table class="col-table">
          <thead>
            <tr>
              <th class="th-toggle">Export</th>
              <th>#</th>
              <th>Source Column</th>
              <th>Export As (target name)</th>
              <th>Type</th>
              <th>PK</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="(field, idx) in fields"
              :key="field.sourceName"
              :class="field.enabled ? 'row-active' : 'row-inactive'"
            >
              <td class="td-toggle">
                <input type="checkbox" v-model="field.enabled" @change="saved = false" />
              </td>
              <td class="col-idx">{{ idx + 1 }}</td>
              <td>
                <code :class="['col-name', field.enabled ? '' : 'col-name-dim']">
                  {{ field.sourceName }}
                </code>
              </td>
              <td class="td-export-as">
                <input
                  class="export-as-input"
                  type="text"
                  :placeholder="field.sourceName"
                  v-model="field.targetName"
                  :disabled="!field.enabled"
                  @input="saved = false"
                />
              </td>
              <td>
                <span class="type-pill">
                  {{ selectedTableColumnMap[field.sourceName]?.type ?? '' }}
                </span>
              </td>
              <td>
                <span v-if="selectedTableColumnMap[field.sourceName]?.primaryKey" class="pk-badge">
                  PK
                </span>
                <span v-else class="no-pk">—</span>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <!-- Relations -->
      <div v-if="selectedTable" class="section">
        <div class="section-header-row">
          <h2>Related Table Joins</h2>
          <button class="add-btn" @click="addRelation">+ Add Relation</button>
        </div>
        <p class="section-desc">
          Add 1:N joins to pull aggregated values from related tables into the export row.
          Use <em>String Join</em> to concatenate values, or <em>Array</em> to comma-separate them.
        </p>

        <div v-if="relations.length === 0" class="empty-hint">
          No relations configured.
        </div>

        <div
          v-for="(rel, idx) in relations"
          :key="idx"
          :class="['relation-card', rel.enabled ? 'rel-active' : 'rel-disabled']"
        >
          <div class="rel-enable-col">
            <input
              type="checkbox"
              v-model="rel.enabled"
              class="rel-checkbox"
              title="Enable this relation"
              @change="saved = false"
            />
          </div>

          <div class="rel-body">
            <div class="rel-row">
              <!-- Related table -->
              <div class="rel-field">
                <label>Related Table</label>
                <select v-model="rel.relatedTable" class="rel-select" @change="saved = false">
                  <option value="" disabled>— select —</option>
                  <option
                    v-for="t in sourceSchema.tables.filter((t) => t.name !== selectedTable)"
                    :key="t.name"
                    :value="t.name"
                  >
                    {{ t.name }}
                  </option>
                </select>
              </div>

              <!-- Source join column -->
              <div class="rel-field">
                <label>Source Column</label>
                <select v-model="rel.sourceJoinKey" class="rel-select" @change="saved = false">
                  <option value="" disabled>— select —</option>
                  <option v-for="c in selectedTableColumns" :key="c.name" :value="c.name">
                    {{ c.name }}{{ c.primaryKey ? ' (PK)' : '' }}
                  </option>
                </select>
              </div>

              <!-- Join column in related table -->
              <div class="rel-field">
                <label>Join Column (in {{ rel.relatedTable || '…' }})</label>
                <select
                  v-model="rel.joinKey"
                  class="rel-select"
                  :disabled="!rel.relatedTable"
                  @change="saved = false"
                >
                  <option value="" disabled>— select —</option>
                  <option
                    v-for="c in getTableColumns(rel.relatedTable)"
                    :key="c.name"
                    :value="c.name"
                  >
                    {{ c.name }}
                  </option>
                </select>
              </div>

              <!-- Target field name -->
              <div class="rel-field">
                <label>Target Field Name</label>
                <input
                  class="rel-input"
                  type="text"
                  placeholder="e.g. maintenance_states"
                  v-model="rel.targetField"
                  @input="saved = false"
                />
              </div>
            </div>

            <div class="rel-row">
              <!-- Value column (source field to aggregate) -->
              <div class="rel-field">
                <label>Value Column (from {{ rel.relatedTable || '…' }})</label>
                <select
                  v-model="rel.strategyOptions.sourceField"
                  class="rel-select"
                  :disabled="!rel.relatedTable"
                  @change="saved = false"
                >
                  <option value="" disabled>— select —</option>
                  <option
                    v-for="c in getTableColumns(rel.relatedTable)"
                    :key="c.name"
                    :value="c.name"
                  >
                    {{ c.name }}
                  </option>
                </select>
              </div>

              <!-- Flatten strategy -->
              <div class="rel-field">
                <label>Flatten Strategy</label>
                <select v-model="rel.flattenStrategy" class="rel-select" @change="saved = false">
                  <option value="string_join">String Join (concatenate with delimiter)</option>
                  <option value="array">Array (comma-separated list)</option>
                </select>
              </div>

              <!-- Delimiter (only for string_join) -->
              <div v-if="rel.flattenStrategy === 'string_join'" class="rel-field rel-field-sm">
                <label>Delimiter</label>
                <input
                  class="rel-input"
                  type="text"
                  v-model="rel.strategyOptions.delimiter"
                  placeholder=", "
                  @input="saved = false"
                />
              </div>
            </div>
          </div>

          <button class="rel-remove-btn" @click="removeRelation(idx)" title="Remove relation">
            ×
          </button>
        </div>
      </div>

      <!-- Format selection -->
      <div v-if="selectedTable" class="section">
        <h2>Export Format</h2>
        <div class="format-row">
          <button
            v-for="fmt in [
              { id: 'xlsx', label: 'Excel (.xlsx)', desc: 'Full format with metadata row — required for ServiceNow Transform Map' },
              { id: 'csv',  label: 'CSV (.csv)',   desc: 'Plain text, comma-separated — compatible with most tools' },
              { id: 'json', label: 'JSON (.json)', desc: 'Machine-readable — useful for APIs and custom pipelines' },
            ]"
            :key="fmt.id"
            :class="['format-btn', selectedFormat === fmt.id ? 'format-active' : '']"
            @click="setFormat(fmt.id as 'xlsx' | 'csv' | 'json')"
          >
            <span class="fmt-label">{{ fmt.label }}</span>
            <span class="fmt-desc">{{ fmt.desc }}</span>
          </button>
        </div>
      </div>

      <!-- Save status + errors -->
      <div v-if="saveError" class="save-error">{{ saveError }}</div>
      <div v-if="saved && !saveError" class="save-ok">Mapping saved.</div>

      <!-- Navigation -->
      <div class="nav-actions">
        <button class="btn-back" @click="router.push({ name: 'source-schema' })">
          ← Back to Source Schema
        </button>
        <div class="nav-right" v-if="selectedTable">
          <button class="btn-save" :disabled="saving" @click="saveMapping">
            {{ saving ? 'Saving…' : 'Save Mapping' }}
          </button>
          <button class="btn-next" :disabled="saving" @click="proceed">
            Run Export ({{ selectedFormat.toUpperCase() }}) →
          </button>
        </div>
      </div>
    </template>
  </div>
</template>

<style scoped>
.page {
  max-width: 960px;
}

.step-header {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-bottom: 0.5rem;
}

.step-badge {
  background: #1a1a2e;
  color: #e2e8f0;
  padding: 0.2rem 0.6rem;
  border-radius: 9999px;
  font-size: 0.75rem;
  font-weight: 700;
  letter-spacing: 0.04em;
  flex-shrink: 0;
}

h1 {
  margin: 0;
  font-size: 1.25rem;
  flex: 1;
}

h2 {
  font-size: 1rem;
  font-weight: 600;
  margin: 0 0 0.6rem;
  color: #1e293b;
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.section {
  margin-bottom: 1.75rem;
}

.section-header-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 0.5rem;
}

.section-header-row h2 {
  margin-bottom: 0;
}

.section-meta {
  font-size: 0.8rem;
  font-weight: 500;
  color: #64748b;
  background: #f1f5f9;
  padding: 0.1rem 0.45rem;
  border-radius: 9999px;
}

.section-desc {
  font-size: 0.83rem;
  color: #64748b;
  margin: 0 0 0.75rem;
  line-height: 1.5;
}

.refresh-btn {
  padding: 0.25rem 0.75rem;
  border: 1px solid #cbd5e1;
  border-radius: 0.375rem;
  background: #fff;
  font-size: 0.8rem;
  cursor: pointer;
  color: #475569;
}
.refresh-btn:disabled { opacity: 0.5; cursor: not-allowed; }

.intro {
  color: #475569;
  font-size: 0.9rem;
  margin: 0.5rem 0 1.5rem;
  line-height: 1.6;
}

/* Table selector */
.table-select-row {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex-wrap: wrap;
}

.table-select {
  padding: 0.4rem 0.7rem;
  border: 1px solid #cbd5e1;
  border-radius: 0.375rem;
  font-size: 0.875rem;
  color: #1e293b;
  background: #fff;
  cursor: pointer;
  min-width: 220px;
}

.conn-chip {
  font-size: 0.78rem;
  color: #64748b;
  background: #f1f5f9;
  padding: 0.2rem 0.6rem;
  border-radius: 9999px;
  border: 1px solid #e2e8f0;
}

/* Column table */
.col-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.875rem;
}

.col-table th,
.col-table td {
  padding: 0.45rem 0.65rem;
  text-align: left;
  border-bottom: 1px solid #e2e8f0;
  vertical-align: middle;
}

.col-table th {
  background: #f8fafc;
  font-weight: 600;
  font-size: 0.72rem;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  white-space: nowrap;
  color: #64748b;
}

.th-toggle,
.td-toggle {
  width: 3rem;
  text-align: center;
}

.row-active td { background: #f0fdf4; }
.row-inactive td { background: #fafafa; opacity: 0.6; }

.col-idx { color: #94a3b8; font-size: 0.78rem; text-align: center; width: 2rem; }

.col-name { font-size: 0.85rem; font-weight: 600; color: #1e293b; }
.col-name-dim { color: #94a3b8; }

.td-export-as { min-width: 10rem; }

.export-as-input {
  width: 100%;
  padding: 0.2rem 0.4rem;
  border: 1px solid #cbd5e1;
  border-radius: 0.25rem;
  font-size: 0.82rem;
  font-family: monospace;
  color: #1e293b;
  background: #fff;
  box-sizing: border-box;
}
.export-as-input:focus { outline: none; border-color: #1a1a2e; }
.export-as-input::placeholder { color: #94a3b8; }
.export-as-input:disabled { background: #f8fafc; }

.type-pill {
  display: inline-block;
  padding: 0.1rem 0.4rem;
  background: #f1f5f9;
  border: 1px solid #cbd5e1;
  border-radius: 0.25rem;
  font-size: 0.73rem;
  color: #475569;
  white-space: nowrap;
}

.pk-badge {
  font-size: 0.65rem;
  font-weight: 700;
  background: #dbeafe;
  color: #1d4ed8;
  padding: 0.1rem 0.35rem;
  border-radius: 0.2rem;
}

.no-pk { color: #cbd5e1; }

/* Relations */
.add-btn {
  padding: 0.3rem 0.8rem;
  border: 1px solid #cbd5e1;
  border-radius: 0.375rem;
  background: #fff;
  font-size: 0.82rem;
  color: #475569;
  cursor: pointer;
  white-space: nowrap;
}
.add-btn:hover { background: #f1f5f9; }

.empty-hint {
  font-size: 0.85rem;
  color: #94a3b8;
  padding: 0.75rem 1rem;
  border: 1px dashed #cbd5e1;
  border-radius: 0.375rem;
  text-align: center;
}

.relation-card {
  display: flex;
  gap: 0.75rem;
  align-items: flex-start;
  padding: 0.85rem 1rem;
  border: 1px solid #e2e8f0;
  border-radius: 0.5rem;
  margin-bottom: 0.6rem;
  background: #fff;
}

.rel-active { border-color: #bfdbfe; background: #f0f9ff; }
.rel-disabled { opacity: 0.65; }

.rel-enable-col {
  padding-top: 0.25rem;
  flex-shrink: 0;
}

.rel-checkbox { cursor: pointer; width: 1rem; height: 1rem; }

.rel-body { flex: 1; display: flex; flex-direction: column; gap: 0.5rem; }

.rel-row {
  display: flex;
  gap: 0.65rem;
  flex-wrap: wrap;
}

.rel-field {
  display: flex;
  flex-direction: column;
  gap: 0.2rem;
  flex: 1;
  min-width: 140px;
}

.rel-field-sm { flex: 0 0 100px; min-width: 80px; }

.rel-field label {
  font-size: 0.7rem;
  font-weight: 600;
  color: #64748b;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  white-space: nowrap;
}

.rel-select,
.rel-input {
  padding: 0.3rem 0.5rem;
  border: 1px solid #cbd5e1;
  border-radius: 0.3rem;
  font-size: 0.82rem;
  color: #1e293b;
  background: #fff;
  width: 100%;
  box-sizing: border-box;
}
.rel-select:disabled { background: #f8fafc; color: #94a3b8; }
.rel-select:focus,
.rel-input:focus { outline: none; border-color: #1a1a2e; }

.rel-remove-btn {
  flex-shrink: 0;
  padding: 0.25rem 0.55rem;
  border: 1px solid #fecaca;
  border-radius: 0.3rem;
  background: #fff;
  color: #dc2626;
  font-size: 1rem;
  line-height: 1;
  cursor: pointer;
}
.rel-remove-btn:hover { background: #fef2f2; }

/* Format buttons */
.format-row {
  display: flex;
  gap: 0.75rem;
}

.format-btn {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 0.2rem;
  padding: 0.75rem 1rem;
  border: 2px solid #e2e8f0;
  border-radius: 0.5rem;
  background: #fff;
  cursor: pointer;
  text-align: left;
  transition: border-color 0.1s;
}
.format-btn:hover { border-color: #94a3b8; }
.format-active { border-color: #1a1a2e; background: #f8fafc; }

.fmt-label { font-size: 0.9rem; font-weight: 600; color: #1e293b; }
.fmt-desc  { font-size: 0.78rem; color: #64748b; line-height: 1.4; }

/* Save status */
.save-error {
  padding: 0.6rem 0.9rem;
  background: #fef2f2;
  border: 1px solid #fecaca;
  border-radius: 0.375rem;
  font-size: 0.85rem;
  color: #dc2626;
  margin-bottom: 1rem;
}

.save-ok {
  padding: 0.6rem 0.9rem;
  background: #f0fdf4;
  border: 1px solid #bbf7d0;
  border-radius: 0.375rem;
  font-size: 0.85rem;
  color: #166534;
  margin-bottom: 1rem;
}

/* Navigation */
.nav-actions {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-top: 1.5rem;
  gap: 0.75rem;
}

.nav-right {
  display: flex;
  gap: 0.6rem;
}

.btn-back {
  padding: 0.45rem 1rem;
  border: 1px solid #cbd5e1;
  border-radius: 0.375rem;
  background: #fff;
  color: #475569;
  font-size: 0.875rem;
  cursor: pointer;
}
.btn-back:hover { background: #f1f5f9; }

.btn-save {
  padding: 0.45rem 1rem;
  border: 1px solid #1a1a2e;
  border-radius: 0.375rem;
  background: #fff;
  color: #1a1a2e;
  font-size: 0.875rem;
  font-weight: 600;
  cursor: pointer;
}
.btn-save:hover:not(:disabled) { background: #f8fafc; }
.btn-save:disabled { opacity: 0.5; cursor: not-allowed; }

.btn-next {
  padding: 0.45rem 1.25rem;
  border: none;
  border-radius: 0.375rem;
  background: #1a1a2e;
  color: #e2e8f0;
  font-size: 0.875rem;
  font-weight: 600;
  cursor: pointer;
}
.btn-next:hover:not(:disabled) { background: #2d2d4e; }
.btn-next:disabled { opacity: 0.5; cursor: not-allowed; }

.info  { color: #64748b; }
.error { color: #dc2626; }
</style>
