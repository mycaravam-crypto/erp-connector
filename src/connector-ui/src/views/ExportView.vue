<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { RouterLink } from 'vue-router'
import { runNow, getPreview, type RunNowResult, type PreviewResult } from '@/api/pipeline'
import { getExportMapping, type ExportMappingConfig } from '@/api/mapping'
import { listExports, type ExportSummary } from '@/api/exports'
import ActiveMappingSummary from '@/components/ActiveMappingSummary.vue'
import PreviewTable from '@/components/PreviewTable.vue'
import ExportRunsTable from '@/components/ExportRunsTable.vue'
import { Check, X } from 'lucide-vue-next'
import Icon from '@/components/ui/Icon.vue'
import Button from '@/components/ui/Button.vue'
import Alert from '@/components/ui/Alert.vue'

const FORMAT_KEY = 'connector_export_format'
const selectedFormat = ref<'xlsx' | 'csv' | 'json'>(
  (localStorage.getItem(FORMAT_KEY) as 'xlsx' | 'csv' | 'json') ?? 'json',
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
</script>

<template>
  <div class="max-w-5xl">
    <div class="flex items-center gap-3 mb-2">
      <span class="bg-brand text-white px-2.5 py-0.5 rounded-full text-xs font-bold tracking-wide">Step 4</span>
      <h1 class="m-0 text-xl font-semibold text-text-primary">Export</h1>
    </div>

    <p class="text-text-secondary text-sm mt-2 mb-5 leading-relaxed">
      Choose your export format, preview the data, then trigger the export. Each export run
      is logged below and requires a four-eyes release before it is considered final.
    </p>

    <!-- Format + Run card -->
    <div class="border border-border rounded-lg overflow-hidden mb-8">
      <div class="flex items-center justify-between gap-6 px-6 py-5">
        <div class="flex-1">
          <h2 class="m-0 text-base font-semibold text-text-primary mb-2.5">Export Format</h2>
          <div class="flex gap-2">
            <button
              v-for="fmt in [
                { id: 'xlsx', label: 'Excel', ext: '.xlsx' },
                { id: 'csv',  label: 'CSV',   ext: '.csv'  },
                { id: 'json', label: 'JSON',  ext: '.json' },
              ]"
              :key="fmt.id"
              :class="['flex items-center gap-1.5 px-3 py-1.5 border rounded-md text-sm font-semibold cursor-pointer transition-colors', selectedFormat === fmt.id ? 'border-brand bg-brand text-white' : 'border-border-strong bg-surface text-text-secondary hover:border-brand']"
              @click="saveFormat(fmt.id as 'xlsx' | 'csv' | 'json')"
            >
              {{ fmt.label }}<span class="text-xs opacity-70">{{ fmt.ext }}</span>
            </button>
          </div>
        </div>
        <Button variant="primary" class="shrink-0 whitespace-nowrap" :loading="running" @click="triggerRun">
          {{ running ? 'Running…' : `Export as ${selectedFormat.toUpperCase()}` }}
        </Button>
      </div>

      <Alert v-if="runResult" variant="success" class="rounded-none border-x-0 border-b-0">
        <template #icon><Icon :icon="Check" :size="20" /></template>
        <span class="flex-1">
          Export <strong>#{{ runResult.sequenceNo }}</strong> created —
          {{ runResult.recordCount }} records ·
          <code>{{ runResult.sha256Short }}…</code>
        </span>
        <RouterLink
          :to="{ name: 'export-detail', params: { seqNo: runResult.sequenceNo } }"
          class="text-inherit text-xs font-semibold no-underline border border-current rounded px-2 py-0.5 hover:opacity-75 ml-3"
        >View → Release</RouterLink>
      </Alert>

      <Alert v-if="runError" variant="danger" class="rounded-none border-x-0 border-b-0">
        <template #icon><Icon :icon="X" :size="20" /></template>
        {{ runError }}
      </Alert>
    </div>

    <ActiveMappingSummary :mapping="exportMapping" :loading="mappingLoading" />

    <PreviewTable
      :preview="preview"
      :loading="previewLoading"
      :error="previewError"
      @refresh="loadPreview"
    />

    <ExportRunsTable
      :runs="runs"
      :loading="runsLoading"
      :error="runsError"
      @refresh="loadRuns"
    />
  </div>
</template>
