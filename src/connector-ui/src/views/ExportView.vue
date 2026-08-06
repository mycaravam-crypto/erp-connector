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
    previewError.value = 'Could not reach the API. Is the backend service running?'
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
    runError.value = 'Could not reach the API. Is the backend service running?'
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
const enabledRelationFields = computed(() =>
  enabledRelations.value.flatMap((r) =>
    r.fields.filter((f) => f.enabled).map((f) => ({ relatedTable: r.relatedTable, ...f })),
  ),
)

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
const PREVIEW_MAX = 20
const previewRows = computed(() => preview.value?.records.slice(0, PREVIEW_MAX) ?? [])

function previewVal(rec: PreviewResult['records'][number], col: string): string {
  return rec[col] ?? '—'
}

function formatDate(iso: string | null | undefined): string {
  if (!iso) return '—'
  return new Date(iso.includes('Z') ? iso : iso + 'Z').toLocaleString()
}

const copiedSeqNo = ref<number | null>(null)
async function copySha(seqNo: number, hash: string) {
  await navigator.clipboard.writeText(hash)
  copiedSeqNo.value = seqNo
  setTimeout(() => { copiedSeqNo.value = null }, 1500)
}
</script>

<template>
  <div class="max-w-5xl">
    <div class="flex items-center gap-3 mb-2">
      <span class="bg-slate-900 text-slate-200 px-2.5 py-0.5 rounded-full text-xs font-bold tracking-wide">Step 4</span>
      <h1 class="m-0 text-xl font-semibold">Export</h1>
    </div>

    <p class="text-slate-500 text-sm mt-2 mb-5 leading-relaxed">
      Choose your export format, preview the data, then trigger the export. Each export run
      is logged below and requires a four-eyes release before it is considered final.
    </p>

    <!-- Format + Run card -->
    <div class="border border-slate-200 rounded-lg overflow-hidden mb-8">
      <div class="flex items-center justify-between gap-6 px-6 py-5">
        <div class="flex-1">
          <h2 class="m-0 text-base font-semibold text-slate-900 mb-2.5">Export Format</h2>
          <div class="flex gap-2">
            <button
              v-for="fmt in [
                { id: 'xlsx', label: 'Excel', ext: '.xlsx' },
                { id: 'csv',  label: 'CSV',   ext: '.csv'  },
                { id: 'json', label: 'JSON',  ext: '.json' },
              ]"
              :key="fmt.id"
              :class="['flex items-center gap-1.5 px-3 py-1.5 border rounded-md text-sm font-semibold cursor-pointer', selectedFormat === fmt.id ? 'border-slate-900 bg-slate-900 text-slate-200' : 'border-slate-300 bg-white text-slate-500 hover:border-slate-400']"
              @click="saveFormat(fmt.id as 'xlsx' | 'csv' | 'json')"
            >
              {{ fmt.label }}<span class="text-xs opacity-70">{{ fmt.ext }}</span>
            </button>
          </div>
        </div>
        <button
          class="shrink-0 px-6 py-2 bg-slate-900 text-slate-200 border-0 rounded-md text-sm font-semibold cursor-pointer whitespace-nowrap hover:enabled:bg-slate-800 disabled:opacity-50 disabled:cursor-not-allowed"
          :disabled="running"
          @click="triggerRun"
        >
          {{ running ? 'Running…' : `Export as ${selectedFormat.toUpperCase()}` }}
        </button>
      </div>

      <div v-if="runResult" class="flex items-center gap-3 px-6 py-3 bg-green-50 border-t border-green-200 text-green-800 text-sm">
        <span class="font-bold text-base">✓</span>
        <span class="flex-1">
          Export <strong>#{{ runResult.sequenceNo }}</strong> created —
          {{ runResult.recordCount }} records ·
          <code>{{ runResult.sha256Short }}…</code>
        </span>
        <RouterLink
          :to="{ name: 'export-detail', params: { seqNo: runResult.sequenceNo } }"
          class="text-inherit text-xs font-semibold no-underline border border-current rounded px-2 py-0.5 hover:opacity-75"
        >View → Release</RouterLink>
      </div>

      <div v-if="runError" class="flex items-center gap-3 px-6 py-3 bg-red-50 border-t border-red-200 text-red-800 text-sm">
        <span class="font-bold text-base">✕</span>
        <span>{{ runError }}</span>
      </div>
    </div>

    <!-- Active mapping summary -->
    <div v-if="!mappingLoading" class="bg-slate-50 border border-slate-200 rounded-lg px-5 py-4 mb-8">
      <div class="flex items-center gap-3 mb-1">
        <h2 class="m-0 text-base font-semibold text-slate-900">Active Mapping</h2>
        <span v-if="exportMapping" class="inline-block px-2 py-0.5 rounded-full text-xs font-bold uppercase tracking-wide bg-green-100 text-green-700">Live Postgres</span>
        <span v-else class="inline-block px-2 py-0.5 rounded-full text-xs font-bold uppercase tracking-wide bg-slate-100 text-slate-500">Not configured</span>
        <RouterLink v-if="exportMapping" :to="{ name: 'export-schema' }" class="ml-auto px-2.5 py-1 border border-slate-300 rounded-md bg-white text-xs text-slate-500 no-underline hover:bg-slate-50">Edit in Step 3 →</RouterLink>
        <RouterLink v-else :to="{ name: 'export-schema' }" class="ml-auto px-2.5 py-1 border border-slate-300 rounded-md bg-white text-xs text-slate-500 no-underline hover:bg-slate-50">Configure in Step 3 →</RouterLink>
      </div>

      <div v-if="exportMapping">
        <p class="text-sm text-slate-500 m-0 mb-2">
          Source table: <strong class="text-slate-900">{{ exportMapping.sourceTable }}</strong>
          &nbsp;·&nbsp;{{ enabledFields.length }} field<span v-if="enabledFields.length !== 1">s</span>
          <template v-if="enabledRelations.length > 0">
            &nbsp;+&nbsp;{{ enabledRelations.length }} relation<span v-if="enabledRelations.length !== 1">s</span>
          </template>
        </p>
        <div class="flex flex-wrap gap-1.5">
          <span v-for="f in enabledFields" :key="f.sourceName" class="inline-flex items-center gap-1 bg-white border border-slate-300 rounded-full px-2.5 py-0.5 text-xs">
            <span class="text-slate-500">{{ f.sourceName }}</span>
            <template v-if="f.targetName !== f.sourceName">
              <span class="text-slate-300 text-xs">→</span>
              <span class="text-slate-900 font-semibold">{{ f.targetName }}</span>
            </template>
          </span>
          <span v-for="f in enabledRelationFields" :key="`${f.relatedTable}.${f.sourceField}`" class="inline-flex items-center gap-1 bg-indigo-50 border border-indigo-200 rounded-full px-2.5 py-0.5 text-xs">
            <span class="text-slate-500">{{ f.relatedTable }}.{{ f.sourceField }}</span>
            <span class="text-slate-300 text-xs">→</span>
            <span class="text-slate-900 font-semibold">{{ f.targetField }}</span>
          </span>
        </div>
      </div>
      <p v-else class="text-sm text-slate-500 m-0">
        No export mapping saved yet. Configure one in Step 3 before running an export.
      </p>
    </div>

    <!-- Preview -->
    <div class="mb-8">
      <div class="flex items-center gap-3 mb-1">
        <h2 class="m-0 text-base font-semibold text-slate-900">Preview</h2>
        <span v-if="preview" class="text-xs text-slate-500">{{ preview.recordCount >= 50 ? '50+' : preview.recordCount }} records (preview) · schema v{{ preview.schemaVersion }}</span>
        <span v-if="preview?.source === 'error'" class="inline-block bg-orange-50 border border-orange-200 rounded-full px-2 py-0.5 text-xs font-semibold text-orange-700">Preview failed</span>
        <button class="ml-auto px-2.5 py-1 border border-slate-300 rounded-md bg-white text-xs text-slate-500 cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed hover:enabled:bg-slate-50" :disabled="previewLoading" @click="loadPreview">Refresh</button>
      </div>
      <p class="text-xs text-slate-500 m-0 mb-3">Read-only view of what the next export will contain. Nothing is written to disk.</p>

      <p v-if="previewLoading" class="text-slate-500 text-sm">Loading preview…</p>
      <p v-else-if="previewError" class="text-red-600 text-sm">{{ previewError }}</p>

      <div v-else-if="preview?.source === 'error'" class="bg-red-50 border border-red-200 rounded-md px-4 py-3">
        <p class="m-0 mb-1 text-sm text-red-700 font-semibold">{{ preview.error }}</p>
        <p class="m-0 text-xs text-red-600">Check your connection (Step 1) and make sure at least one column is enabled in Step 3.</p>
      </div>

      <div v-else-if="preview && preview.records.length > 0" class="overflow-x-auto max-h-80 overflow-y-auto border border-slate-200 rounded-md">
        <table class="w-full border-collapse text-sm">
          <thead class="sticky top-0">
            <tr>
              <th class="px-2.5 py-2 text-left bg-slate-50 font-semibold text-[0.7rem] uppercase tracking-wide border-b border-slate-200 whitespace-nowrap">#</th>
              <th v-for="col in previewCols" :key="col" class="px-2.5 py-2 text-left bg-slate-50 font-semibold text-[0.7rem] uppercase tracking-wide border-b border-slate-200 whitespace-nowrap">{{ col }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="(rec, idx) in previewRows" :key="idx" class="hover:bg-slate-50">
              <td class="px-2.5 py-1.5 border-b border-slate-200 align-middle text-center text-slate-400 text-xs w-8">{{ idx + 1 }}</td>
              <td v-for="col in previewCols" :key="col" class="px-2.5 py-1.5 border-b border-slate-200 align-middle whitespace-nowrap">{{ previewVal(rec, col) }}</td>
            </tr>
          </tbody>
        </table>
      </div>
      <p v-if="preview && preview.records.length > PREVIEW_MAX" class="text-xs text-slate-400 mt-1.5">
        Showing first {{ PREVIEW_MAX }} rows — preview is capped at 50; the full export includes all in-scope records.
      </p>

      <p v-else-if="preview" class="text-slate-500 text-sm">No in-scope records found.</p>
    </div>

    <!-- Export run history -->
    <div class="mb-8">
      <div class="flex items-center gap-3 mb-3">
        <h2 class="m-0 text-base font-semibold text-slate-900">Export Runs</h2>
        <button class="ml-auto px-2.5 py-1 border border-slate-300 rounded-md bg-white text-xs text-slate-500 cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed hover:enabled:bg-slate-50" :disabled="runsLoading" @click="loadRuns">Refresh</button>
      </div>

      <div v-if="!runsLoading && runs.some((r) => r.isStale)" class="bg-orange-50 border border-orange-200 rounded-md px-4 py-2.5 text-sm text-orange-900 mb-3" role="alert">
        <strong>Action required:</strong> one or more export runs have been pending for over 24 hours.
        Please review and release or investigate.
      </div>

      <p v-if="runsLoading" class="text-slate-500 text-sm">Loading…</p>
      <p v-else-if="runsError" class="text-red-600 text-sm">{{ runsError }}</p>
      <p v-else-if="runs.length === 0" class="text-slate-500 text-sm">No export runs yet.</p>

      <table v-else class="w-full border-collapse text-sm">
        <thead>
          <tr>
            <th class="px-2.5 py-2 text-left bg-slate-50 font-semibold text-[0.7rem] uppercase tracking-wide border-b border-slate-200 whitespace-nowrap">#</th>
            <th class="px-2.5 py-2 text-left bg-slate-50 font-semibold text-[0.7rem] uppercase tracking-wide border-b border-slate-200 whitespace-nowrap">Extracted At</th>
            <th class="px-2.5 py-2 text-left bg-slate-50 font-semibold text-[0.7rem] uppercase tracking-wide border-b border-slate-200 whitespace-nowrap">Records</th>
            <th class="px-2.5 py-2 text-left bg-slate-50 font-semibold text-[0.7rem] uppercase tracking-wide border-b border-slate-200 whitespace-nowrap">SHA-256</th>
            <th class="px-2.5 py-2 text-left bg-slate-50 font-semibold text-[0.7rem] uppercase tracking-wide border-b border-slate-200 whitespace-nowrap">Status</th>
            <th class="px-2.5 py-2 text-left bg-slate-50 font-semibold text-[0.7rem] uppercase tracking-wide border-b border-slate-200 whitespace-nowrap">File</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="run in runs" :key="run.sequenceNo" class="hover:bg-slate-50">
            <td class="px-2.5 py-1.5 border-b border-slate-200 align-middle whitespace-nowrap">
              <RouterLink :to="{ name: 'export-detail', params: { seqNo: run.sequenceNo } }" class="text-indigo-600 no-underline hover:underline">{{ run.sequenceNo }}</RouterLink>
            </td>
            <td class="px-2.5 py-1.5 border-b border-slate-200 align-middle whitespace-nowrap">{{ formatDate(run.extractedAt) }}</td>
            <td class="px-2.5 py-1.5 border-b border-slate-200 align-middle whitespace-nowrap">{{ run.recordCount }}</td>
            <td class="px-2.5 py-1.5 border-b border-slate-200 align-middle whitespace-nowrap">
              <button
                class="group inline-flex items-center gap-1 bg-transparent border-0 p-0 cursor-pointer"
                :title="copiedSeqNo === run.sequenceNo ? 'Copied!' : 'Click to copy full hash'"
                @click="copySha(run.sequenceNo, run.sha256Short)"
              >
                <code class="text-xs text-slate-500 group-hover:text-slate-800">{{ run.sha256Short }}</code>
                <span class="text-[0.65rem] text-slate-300 group-hover:text-slate-500">{{ copiedSeqNo === run.sequenceNo ? '✓' : '⎘' }}</span>
              </button>
            </td>
            <td class="px-2.5 py-1.5 border-b border-slate-200 align-middle whitespace-nowrap">
              <StatusBadge :status="run.status" />
              <span v-if="run.isStale" class="ml-1.5 inline-block bg-orange-50 border border-orange-200 rounded-full px-1.5 py-0.5 text-[0.65rem] font-bold uppercase tracking-wide text-orange-700 align-middle" title="Pending for over 24 hours">overdue</span>
            </td>
            <td class="px-2.5 py-1.5 border-b border-slate-200 align-middle text-xs text-slate-500 whitespace-nowrap">{{ run.dataFileName }}</td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>
