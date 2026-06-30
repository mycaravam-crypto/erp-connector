<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { RouterLink } from 'vue-router'
import { runNow, getPreview, type RunNowResult, type PreviewResult } from '@/api/pipeline'
import { getExportMapping, type ExportMappingConfig } from '@/api/erp'
import { listExports, type ExportSummary } from '@/api/exports'
import StatusBadge from '@/components/StatusBadge.vue'

const FORMAT_KEY = 'connector_export_format'
const selectedFormat = ref<'xlsx' | 'csv' | 'json'>(
  (localStorage.getItem(FORMAT_KEY) as 'xlsx' | 'csv' | 'json') ?? 'xlsx',
)

function saveFormat(fmt: 'xlsx' | 'csv' | 'json') {
  selectedFormat.value = fmt
  localStorage.setItem(FORMAT_KEY, fmt)
}

// ── preview ────────────────────────────────────────────────────────────────────
const preview = ref<PreviewResult | null>(null)
const previewLoading = ref(true)
const previewError = ref<string | null>(null)

async function loadPreview() {
  previewLoading.value = true
  previewError.value = null
  try {
    preview.value = await getPreview()
    if (!preview.value) previewError.value = 'Preview endpoint returned no data.'
  } catch {
    previewError.value = 'Could not reach the API. Is the backend running on :5189?'
  } finally {
    previewLoading.value = false
  }
}

// ── run ────────────────────────────────────────────────────────────────────────
const running = ref(false)
const runResult = ref<RunNowResult | null>(null)
const runError = ref<string | null>(null)

async function triggerRun() {
  running.value = true
  runResult.value = null
  runError.value = null
  try {
    const res = await runNow(selectedFormat.value)
    if (res.ok && res.data) {
      runResult.value = res.data
      await Promise.all([loadPreview(), loadRuns()])
    } else {
      runError.value = res.error ?? 'Unknown error'
    }
  } catch {
    runError.value = 'Could not reach the API. Is the backend running on :5189?'
  } finally {
    running.value = false
  }
}

// ── active mapping config ──────────────────────────────────────────────────────
const exportMapping = ref<ExportMappingConfig | null>(null)
const mappingLoading = ref(true)

async function loadMapping() {
  mappingLoading.value = true
  try {
    exportMapping.value = await getExportMapping()
  } finally {
    mappingLoading.value = false
  }
}

const enabledFields = computed(() => exportMapping.value?.fields.filter(f => f.enabled) ?? [])
const enabledRelations = computed(() => exportMapping.value?.relations.filter(r => r.enabled) ?? [])

// ── export runs ────────────────────────────────────────────────────────────────
const runs = ref<ExportSummary[]>([])
const runsLoading = ref(true)
const runsError = ref<string | null>(null)

async function loadRuns() {
  runsLoading.value = true
  runsError.value = null
  try {
    runs.value = await listExports()
  } catch {
    runsError.value = 'Could not reach the API.'
  } finally {
    runsLoading.value = false
  }
}

onMounted(() => {
  loadPreview()
  loadRuns()
  loadMapping()
})

const previewCols = computed(() => preview.value?.columns ?? [])

function previewVal(rec: PreviewResult['records'][number], col: string): string {
  return rec[col] ?? '—'
}
</script>

<template>
  <div class="page">
    <div class="step-header">
      <span class="step-badge">Step 4</span>
      <h1>Export</h1>
    </div>

    <p class="intro">
      Choose your export format, preview the data, then trigger the export. Each export run
      is logged below and requires a four-eyes release before it is considered final.
    </p>

    <!-- Format + Run card -->
    <div class="run-card">
      <div class="run-card-body">
        <div class="run-info">
          <h2>Export Format</h2>
          <div class="format-row">
            <button
              v-for="fmt in [
                { id: 'xlsx', label: 'Excel', ext: '.xlsx' },
                { id: 'csv',  label: 'CSV',   ext: '.csv'  },
                { id: 'json', label: 'JSON',  ext: '.json' },
              ]"
              :key="fmt.id"
              :class="['fmt-btn', selectedFormat === fmt.id ? 'fmt-active' : '']"
              @click="saveFormat(fmt.id as 'xlsx' | 'csv' | 'json')"
            >
              {{ fmt.label }}
              <span class="fmt-ext">{{ fmt.ext }}</span>
            </button>
          </div>
        </div>
        <button class="run-btn" :disabled="running" @click="triggerRun">
          {{ running ? 'Running…' : `Export as ${selectedFormat.toUpperCase()}` }}
        </button>
      </div>

      <div v-if="runResult" class="result-banner result-ok">
        <span class="result-icon">✓</span>
        <span class="result-text">
          Export <strong>#{{ runResult.sequenceNo }}</strong> created —
          {{ runResult.recordCount }} records ·
          <code>{{ runResult.sha256Short }}…</code>
        </span>
        <RouterLink
          :to="{ name: 'export-detail', params: { seqNo: runResult.sequenceNo } }"
          class="result-link"
        >
          View → Release
        </RouterLink>
      </div>

      <div v-if="runError" class="result-banner result-err">
        <span class="result-icon">✕</span>
        <span class="result-text">{{ runError }}</span>
      </div>
    </div>

    <!-- Active mapping summary -->
    <div v-if="!mappingLoading" class="section mapping-card">
      <div class="section-header">
        <h2>Active Mapping</h2>
        <span v-if="exportMapping" class="src-badge src-dynamic">Live Postgres</span>
        <span v-else class="src-badge src-demo">Not configured</span>
        <RouterLink v-if="exportMapping" :to="{ name: 'export-schema' }" class="icon-btn" style="margin-left:auto">
          Edit in Step 3 →
        </RouterLink>
        <RouterLink v-else :to="{ name: 'export-schema' }" class="icon-btn" style="margin-left:auto">
          Configure in Step 3 →
        </RouterLink>
      </div>

      <div v-if="exportMapping" class="mapping-body">
        <p class="section-desc">
          Source table: <strong>{{ exportMapping.sourceTable }}</strong>
          &nbsp;·&nbsp;{{ enabledFields.length }} field<span v-if="enabledFields.length !== 1">s</span>
          <template v-if="enabledRelations.length > 0">
            &nbsp;+&nbsp;{{ enabledRelations.length }} relation<span v-if="enabledRelations.length !== 1">s</span>
          </template>
        </p>
        <div class="col-chips">
          <span v-for="f in enabledFields" :key="f.sourceName" class="col-chip">
            <span class="chip-src">{{ f.sourceName }}</span>
            <template v-if="f.targetName !== f.sourceName">
              <span class="chip-arrow">→</span>
              <span class="chip-tgt">{{ f.targetName }}</span>
            </template>
          </span>
          <span v-for="r in enabledRelations" :key="r.targetField" class="col-chip col-chip-rel">
            <span class="chip-src">{{ r.relatedTable }}</span>
            <span class="chip-arrow">→</span>
            <span class="chip-tgt">{{ r.targetField }}</span>
          </span>
        </div>
      </div>
      <p v-else class="info">
        No export mapping saved yet. Configure one in Step 3 before running an export.
      </p>
    </div>

    <!-- Preview -->
    <div class="section">
      <div class="section-header">
        <h2>Preview</h2>
        <span v-if="preview" class="section-meta">
          {{ preview.recordCount }} records · schema v{{ preview.schemaVersion }}
        </span>
        <span
          v-if="preview?.source === 'error'"
          class="fallback-warn"
        >
          Preview failed
        </span>
        <button class="icon-btn" :disabled="previewLoading" @click="loadPreview">Refresh</button>
      </div>
      <p class="section-desc">
        Read-only view of what the next export will contain. Nothing is written to disk.
      </p>

      <p v-if="previewLoading" class="info">Loading preview…</p>
      <p v-else-if="previewError" class="error">{{ previewError }}</p>

      <div v-else-if="preview?.source === 'error'" class="preview-error-box">
        <p class="preview-error-msg">{{ preview.error }}</p>
        <p class="preview-error-hint">
          Check your connection (Step 1) and make sure at least one column is enabled in Step 3.
        </p>
      </div>

      <div v-else-if="preview && preview.records.length > 0" class="table-wrap">
        <table>
          <thead>
            <tr>
              <th>#</th>
              <th v-for="col in previewCols" :key="col">{{ col }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="(rec, idx) in preview.records" :key="idx">
              <td class="row-idx">{{ idx + 1 }}</td>
              <td v-for="col in previewCols" :key="col">
                <span>{{ previewVal(rec, col) }}</span>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <p v-else-if="preview" class="info">
        No in-scope records found.
      </p>
    </div>

    <!-- Export run history -->
    <div class="section">
      <div class="section-header">
        <h2>Export Runs</h2>
        <button class="icon-btn" :disabled="runsLoading" @click="loadRuns">Refresh</button>
      </div>

      <!-- Stale-pending banner: shown when any run has been awaiting release for > 24 h -->
      <div
        v-if="!runsLoading && runs.some((r) => r.isStale)"
        class="stale-banner"
        role="alert"
      >
        <strong>Action required:</strong> one or more export runs have been pending for over 24 hours.
        Please review and release or investigate.
      </div>

      <p v-if="runsLoading" class="info">Loading…</p>
      <p v-else-if="runsError" class="error">{{ runsError }}</p>
      <p v-else-if="runs.length === 0" class="info">No export runs yet.</p>

      <table v-else>
        <thead>
          <tr>
            <th>#</th>
            <th>Extracted At (UTC)</th>
            <th>Records</th>
            <th>SHA-256</th>
            <th>Status</th>
            <th>File</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="run in runs" :key="run.sequenceNo">
            <td>
              <RouterLink :to="{ name: 'export-detail', params: { seqNo: run.sequenceNo } }">
                {{ run.sequenceNo }}
              </RouterLink>
            </td>
            <td>{{ run.extractedAt }}</td>
            <td>{{ run.recordCount }}</td>
            <td><code class="sha-short">{{ run.sha256Short }}</code></td>
            <td>
              <StatusBadge :status="run.status" />
              <span v-if="run.isStale" class="stale-tag" title="Pending for over 24 hours">overdue</span>
            </td>
            <td class="file-name">{{ run.dataFileName }}</td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<style scoped>
.page {
  max-width: 1000px;
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
}

h1 {
  margin: 0;
  font-size: 1.25rem;
}

h2 {
  margin: 0;
  font-size: 1rem;
  font-weight: 600;
  color: #1e293b;
}

.intro {
  color: #475569;
  font-size: 0.9rem;
  margin: 0.5rem 0 1.25rem;
  line-height: 1.6;
}

/* Run card */
.run-card {
  border: 1px solid #e2e8f0;
  border-radius: 0.5rem;
  overflow: hidden;
  margin-bottom: 2rem;
}

.run-card-body {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1.5rem;
  padding: 1.25rem 1.5rem;
}

.run-info { flex: 1; }

.format-row {
  display: flex;
  gap: 0.5rem;
  margin-top: 0.6rem;
}

.fmt-btn {
  display: flex;
  align-items: center;
  gap: 0.3rem;
  padding: 0.3rem 0.75rem;
  border: 1px solid #cbd5e1;
  border-radius: 0.375rem;
  background: #fff;
  font-size: 0.85rem;
  font-weight: 600;
  cursor: pointer;
  color: #475569;
}

.fmt-btn:hover { border-color: #94a3b8; }

.fmt-active {
  border-color: #1a1a2e;
  background: #1a1a2e;
  color: #e2e8f0;
}

.fmt-ext {
  font-size: 0.72rem;
  opacity: 0.7;
}

.run-btn {
  flex-shrink: 0;
  padding: 0.55rem 1.5rem;
  background: #1a1a2e;
  color: #e2e8f0;
  border: none;
  border-radius: 0.375rem;
  font-size: 0.9rem;
  font-weight: 600;
  cursor: pointer;
  white-space: nowrap;
}

.run-btn:hover:not(:disabled) { background: #2d2d4e; }
.run-btn:disabled { opacity: 0.5; cursor: not-allowed; }

.result-banner {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0.75rem 1.5rem;
  font-size: 0.875rem;
  border-top: 1px solid transparent;
}

.result-ok  { background: #f0fdf4; border-top-color: #bbf7d0; color: #166534; }
.result-err { background: #fef2f2; border-top-color: #fecaca; color: #991b1b; }

.result-icon { font-weight: 700; font-size: 1rem; }
.result-text { flex: 1; }

.result-link {
  color: inherit;
  font-size: 0.82rem;
  font-weight: 600;
  text-decoration: none;
  border: 1px solid currentColor;
  border-radius: 0.3rem;
  padding: 0.2rem 0.6rem;
}

.result-link:hover { opacity: 0.75; }

/* Sections */
.section {
  margin-bottom: 2rem;
}

.section-header {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-bottom: 0.4rem;
}

.section-meta {
  font-size: 0.82rem;
  color: #64748b;
}

.section-desc {
  font-size: 0.82rem;
  color: #64748b;
  margin: 0 0 0.75rem;
}

.icon-btn {
  margin-left: auto;
  padding: 0.2rem 0.65rem;
  border: 1px solid #cbd5e1;
  border-radius: 0.375rem;
  background: #fff;
  font-size: 0.78rem;
  cursor: pointer;
  color: #475569;
}

.icon-btn:disabled { opacity: 0.5; cursor: not-allowed; }

/* Tables */
.table-wrap {
  overflow-x: auto;
}

table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.84rem;
}

th, td {
  padding: 0.42rem 0.6rem;
  text-align: left;
  border-bottom: 1px solid #e2e8f0;
  vertical-align: middle;
  white-space: nowrap;
}

th {
  background: #f8fafc;
  font-weight: 600;
  font-size: 0.7rem;
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

tr:hover td { background: #f8fafc; }

.row-idx {
  color: #94a3b8;
  font-size: 0.76rem;
  text-align: center;
  width: 2rem;
}

.guid-cell { font-size: 0.72rem; color: #475569; }
.sha-short { font-size: 0.8rem; color: #475569; }
.file-name { font-size: 0.8rem; color: #64748b; }

.state-badge {
  display: inline-block;
  padding: 0.1rem 0.4rem;
  border-radius: 9999px;
  font-size: 0.72rem;
  font-weight: 600;
}

.state-active        { background: #dcfce7; color: #166534; }
.state-inrepair      { background: #fef9c3; color: #854d0e; }
.state-decommissioned { background: #f1f5f9; color: #64748b; }

.stale-banner {
  background: #fff7ed;
  border: 1px solid #fed7aa;
  border-radius: 0.375rem;
  padding: 0.65rem 1rem;
  font-size: 0.875rem;
  color: #92400e;
  margin-bottom: 0.75rem;
}

.stale-tag {
  display: inline-block;
  margin-left: 0.4rem;
  padding: 0.1rem 0.35rem;
  background: #fff7ed;
  border: 1px solid #fed7aa;
  border-radius: 9999px;
  font-size: 0.68rem;
  font-weight: 700;
  color: #92400e;
  vertical-align: middle;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.info  { color: #64748b; }
.error { color: #dc2626; }

/* Mapping summary card */
.mapping-card {
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 0.5rem;
  padding: 1rem 1.25rem;
}

.mapping-body { margin-top: 0.25rem; }

.col-chips {
  display: flex;
  flex-wrap: wrap;
  gap: 0.4rem;
  margin-top: 0.5rem;
}

.col-chip {
  display: inline-flex;
  align-items: center;
  gap: 0.25rem;
  background: #fff;
  border: 1px solid #cbd5e1;
  border-radius: 9999px;
  padding: 0.15rem 0.6rem;
  font-size: 0.78rem;
}

.col-chip-rel {
  border-color: #a5b4fc;
  background: #eef2ff;
}

.chip-src  { color: #475569; }
.chip-arrow { color: #94a3b8; font-size: 0.7rem; }
.chip-tgt  { color: #1e293b; font-weight: 600; }

/* Source badges */
.src-badge {
  display: inline-block;
  padding: 0.15rem 0.55rem;
  border-radius: 9999px;
  font-size: 0.72rem;
  font-weight: 700;
  letter-spacing: 0.03em;
  text-transform: uppercase;
}

.src-dynamic { background: #dcfce7; color: #166534; }
.src-demo    { background: #f1f5f9; color: #475569; }

/* Preview error box */
.preview-error-box {
  background: #fef2f2;
  border: 1px solid #fecaca;
  border-radius: 0.375rem;
  padding: 0.75rem 1rem;
}

.preview-error-msg {
  margin: 0 0 0.35rem;
  font-size: 0.875rem;
  color: #991b1b;
  font-weight: 600;
}

.preview-error-hint {
  margin: 0;
  font-size: 0.8rem;
  color: #b91c1c;
}

/* Fallback warning badge */
.fallback-warn {
  display: inline-block;
  background: #fff7ed;
  border: 1px solid #fed7aa;
  border-radius: 9999px;
  padding: 0.12rem 0.55rem;
  font-size: 0.72rem;
  font-weight: 600;
  color: #92400e;
}
</style>
