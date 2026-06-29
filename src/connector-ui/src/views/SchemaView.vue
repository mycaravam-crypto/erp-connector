<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { getSchema, patchSchemaColumns, type SchemaDefinition, type SchemaColumnDef } from '@/api/erp'

const router = useRouter()

const schema = ref<SchemaDefinition | null>(null)
const error = ref<string | null>(null)
const loading = ref(true)

const activeColumns = ref<Set<string>>(new Set())
const saving = ref(false)
const FORMAT_KEY = 'connector_export_format'
const selectedFormat = ref<'xlsx' | 'csv' | 'json'>(
  (localStorage.getItem(FORMAT_KEY) as 'xlsx' | 'csv' | 'json') ?? 'xlsx',
)

async function load() {
  loading.value = true
  error.value = null
  try {
    schema.value = await getSchema()
    if (!schema.value) {
      error.value = 'Schema endpoint returned no data.'
    } else {
      activeColumns.value = new Set(schema.value.columns.filter((c) => c.active).map((c) => c.name))
    }
  } catch {
    error.value = 'Could not reach the API. Is the backend running on :5189?'
  } finally {
    loading.value = false
  }
}

onMounted(load)

async function toggleColumn(name: string) {
  const next = new Set(activeColumns.value)
  if (next.has(name)) next.delete(name)
  else next.add(name)
  activeColumns.value = next
  // Persist to server so the preference survives page reload.
  saving.value = true
  await patchSchemaColumns([...next])
  saving.value = false
}

function setFormat(fmt: 'xlsx' | 'csv' | 'json') {
  selectedFormat.value = fmt
  localStorage.setItem(FORMAT_KEY, fmt)
}

const activeCount = computed(() => activeColumns.value.size)

const orderedColumns = computed((): SchemaColumnDef[] =>
  schema.value ? schema.value.columns : [],
)

function proceed() {
  localStorage.setItem(FORMAT_KEY, selectedFormat.value)
  router.push({ name: 'exports' })
}
</script>

<template>
  <div class="page">
    <div class="step-header">
      <span class="step-badge">Step 3</span>
      <h1>Export Schema</h1>
      <button class="refresh-btn" :disabled="loading" @click="load">Refresh</button>
    </div>

    <p class="intro">
      Configure which columns to include in the export and choose the output format.
      Toggle columns on or off — your preferences are saved automatically.
    </p>

    <p v-if="loading" class="info">Loading…</p>
    <p v-else-if="error" class="error">{{ error }}</p>

    <template v-else-if="schema">
      <!-- Version + active count -->
      <div class="meta-row">
        <div class="version-chip">
          <span class="meta-label">Schema v{{ schema.version }}</span>
        </div>
        <span class="active-chip">{{ activeCount }} / {{ schema.columns.length }} columns active</span>
        <span v-if="saving" class="saving-chip">saving…</span>
      </div>

      <!-- Column configuration table -->
      <h2>Columns</h2>
      <table class="col-table">
        <thead>
          <tr>
            <th class="th-toggle">Export</th>
            <th>#</th>
            <th>Export Column</th>
            <th>Source Field</th>
            <th>Type</th>
            <th>Notes</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="(col, idx) in orderedColumns"
            :key="col.name"
            :class="activeColumns.has(col.name) ? 'row-active' : 'row-inactive'"
          >
            <td class="td-toggle">
              <input
                type="checkbox"
                :checked="activeColumns.has(col.name)"
                @change="toggleColumn(col.name)"
              />
            </td>
            <td class="col-idx">{{ idx + 1 }}</td>
            <td>
              <code :class="['col-name', activeColumns.has(col.name) ? '' : 'col-name-dim']">
                {{ col.name }}
              </code>
            </td>
            <td class="erp-source">{{ col.erpSource }}</td>
            <td><span class="type-pill">{{ col.type }}</span></td>
            <td class="notes">{{ col.notes }}</td>
          </tr>
        </tbody>
      </table>

      <!-- Format selection -->
      <h2>Export Format</h2>
      <div class="format-row">
        <button
          v-for="fmt in [
            { id: 'xlsx', label: 'Excel (.xlsx)', desc: 'Full format with metadata row — required for vendor Transform Map' },
            { id: 'csv', label: 'CSV (.csv)', desc: 'Plain text, comma-separated — compatible with most tools' },
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

      <!-- Fields excluded from export -->
      <div class="excluded-section">
        <h2>Fields in Source Not Exported</h2>
        <table class="excl-table">
          <thead>
            <tr>
              <th>Source Field</th>
              <th>Reason</th>
            </tr>
          </thead>
          <tbody>
            <tr>
              <td><code>systemconfiguration.technician_name</code></td>
              <td class="reason-gdpr">
                <span class="reason-pill pill-gdpr">GDPR Art. 5(1)(c)</span>
                Personal data — stripped before any file is written
              </td>
            </tr>
            <tr>
              <td><code>systemconfiguration.storage_location</code></td>
              <td class="reason-pending">
                <span class="reason-pill pill-pending">Open Point #4</span>
                Vendor entitlement not confirmed — excluded until ICD is updated
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <div class="nav-actions">
        <button class="btn-back" @click="router.push({ name: 'source-schema' })">
          ← Back to Source Schema
        </button>
        <button class="btn-next" @click="proceed">
          Run Export ({{ selectedFormat.toUpperCase() }}) →
        </button>
      </div>
    </template>
  </div>
</template>

<style scoped>
.page {
  max-width: 900px;
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
  margin: 1.5rem 0 0.6rem;
  color: #1e293b;
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
  margin: 0.5rem 0 1.25rem;
  line-height: 1.6;
}

.meta-row {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-bottom: 0.5rem;
}

.version-chip {
  padding: 0.25rem 0.65rem;
  background: #1a1a2e;
  color: #e2e8f0;
  border-radius: 9999px;
  font-size: 0.8rem;
  font-weight: 700;
}

.meta-label { font-size: 0.8rem; }

.active-chip {
  font-size: 0.82rem;
  color: #166534;
  background: #dcfce7;
  padding: 0.15rem 0.55rem;
  border-radius: 9999px;
  font-weight: 600;
}

.saving-chip {
  font-size: 0.78rem;
  color: #64748b;
  background: #f1f5f9;
  padding: 0.15rem 0.55rem;
  border-radius: 9999px;
}

/* Column table */
.col-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.875rem;
  margin-bottom: 0.5rem;
}

.col-table th, .col-table td {
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
}

.th-toggle, .td-toggle {
  width: 3rem;
  text-align: center;
}

.row-active td { background: #f0fdf4; }
.row-inactive td { background: #fafafa; opacity: 0.6; }

.col-idx { color: #94a3b8; font-size: 0.78rem; text-align: center; width: 2rem; }

.col-name { font-size: 0.85rem; font-weight: 600; color: #1e293b; }
.col-name-dim { color: #94a3b8; }

.erp-source { font-size: 0.82rem; color: #475569; font-style: italic; }

.type-pill {
  display: inline-block;
  padding: 0.1rem 0.4rem;
  background: #f1f5f9;
  border: 1px solid #cbd5e1;
  border-radius: 0.25rem;
  font-size: 0.75rem;
  color: #334155;
  white-space: nowrap;
}

.notes { font-size: 0.82rem; color: #475569; }

/* Format selection */
.format-row {
  display: flex;
  gap: 0.75rem;
  margin-bottom: 0.5rem;
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

.format-active {
  border-color: #1a1a2e;
  background: #f8fafc;
}

.fmt-label {
  font-size: 0.9rem;
  font-weight: 600;
  color: #1e293b;
}

.fmt-desc {
  font-size: 0.78rem;
  color: #64748b;
  line-height: 1.4;
}

/* Excluded section */
.excluded-section {
  margin-top: 1.5rem;
  padding-top: 1.5rem;
  border-top: 1px solid #e2e8f0;
}

.excl-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.875rem;
  max-width: 760px;
}

.excl-table th, .excl-table td {
  padding: 0.45rem 0.65rem;
  text-align: left;
  border-bottom: 1px solid #e2e8f0;
  vertical-align: top;
}

.excl-table th {
  background: #f8fafc;
  font-weight: 600;
  font-size: 0.72rem;
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.reason-pill {
  display: inline-block;
  padding: 0.1rem 0.4rem;
  border-radius: 0.25rem;
  font-size: 0.72rem;
  font-weight: 600;
  margin-right: 0.4rem;
}

.pill-gdpr    { background: #fee2e2; color: #991b1b; }
.pill-pending { background: #fef9c3; color: #854d0e; }

.reason-gdpr    { color: #7f1d1d; font-size: 0.85rem; }
.reason-pending { color: #78350f; font-size: 0.85rem; }

/* Nav */
.nav-actions {
  display: flex;
  justify-content: space-between;
  margin-top: 1.5rem;
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

.btn-next:hover { background: #2d2d4e; }

.info  { color: #64748b; }
.error { color: #dc2626; }
</style>
