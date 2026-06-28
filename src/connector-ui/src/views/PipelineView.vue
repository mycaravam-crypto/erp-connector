<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { runNow, getPreview, type RunNowResult, type PreviewResult } from '@/api/pipeline'

const router = useRouter()

// ── preview state ──────────────────────────────────────────────────────────────
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

onMounted(loadPreview)

// ── run now state ──────────────────────────────────────────────────────────────
const running = ref(false)
const runResult = ref<RunNowResult | null>(null)
const runError = ref<string | null>(null)

async function triggerRun() {
  running.value = true
  runResult.value = null
  runError.value = null
  try {
    const res = await runNow()
    if (res.ok && res.data) {
      runResult.value = res.data
      await loadPreview() // refresh preview to show the same data that was just exported
    } else {
      runError.value = res.error ?? 'Unknown error'
    }
  } catch {
    runError.value = 'Could not reach the API. Is the backend running on :5189?'
  } finally {
    running.value = false
  }
}

function goToDetail(seqNo: number) {
  router.push({ name: 'export-detail', params: { seqNo } })
}
</script>

<template>
  <div>
    <div class="toolbar">
      <h1>Pipeline</h1>
    </div>

    <!-- Run Now card -->
    <div class="run-card">
      <div class="run-card-body">
        <div class="run-info">
          <h2>Run Now</h2>
          <p class="run-desc">
            Triggers the full export pipeline immediately — extract, filter, minimize, map, package,
            and write to staging — bypassing the scheduled time. The new run appears in Export Runs
            with status <strong>Pending</strong>, awaiting four-eyes release.
          </p>
        </div>
        <button class="run-btn" :disabled="running" @click="triggerRun">
          {{ running ? 'Running…' : 'Run Now' }}
        </button>
      </div>

      <!-- Success banner -->
      <div v-if="runResult" class="result-banner result-ok">
        <span class="result-icon">✓</span>
        <span class="result-text">
          Export <strong>#{{ runResult.sequenceNo }}</strong> created —
          {{ runResult.recordCount }} records ·
          <code>{{ runResult.sha256Short }}…</code>
        </span>
        <button class="result-link" @click="goToDetail(runResult.sequenceNo)">
          View → Release
        </button>
      </div>

      <!-- Error banner -->
      <div v-if="runError" class="result-banner result-err">
        <span class="result-icon">✕</span>
        <span class="result-text">{{ runError }}</span>
      </div>
    </div>

    <!-- Export preview -->
    <div class="preview-section">
      <div class="preview-header">
        <h2>Export Preview</h2>
        <span v-if="preview" class="preview-meta">
          {{ preview.recordCount }} records · schema v{{ preview.schemaVersion }}
        </span>
        <button class="refresh-btn" :disabled="previewLoading" @click="loadPreview">
          Refresh
        </button>
      </div>
      <p class="preview-desc">
        Read-only view of what the next export would contain — the pipeline runs through
        Map but nothing is written to disk.
      </p>

      <p v-if="previewLoading" class="info">Loading preview…</p>
      <p v-else-if="previewError" class="error">{{ previewError }}</p>

      <table v-else-if="preview && preview.records.length > 0">
        <thead>
          <tr>
            <th>#</th>
            <th>guid</th>
            <th>serial_number</th>
            <th>part_number</th>
            <th>parent_serial_number</th>
            <th>model_reference</th>
            <th>commissioning_date</th>
            <th>maintenance_state</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(rec, idx) in preview.records" :key="rec.guid">
            <td class="row-idx">{{ idx + 1 }}</td>
            <td><code class="guid-cell">{{ rec.guid }}</code></td>
            <td><code>{{ rec.serialNumber || '—' }}</code></td>
            <td><code>{{ rec.partNumber || '—' }}</code></td>
            <td>{{ rec.parentSerialNumber || '—' }}</td>
            <td>{{ rec.modelReference || '—' }}</td>
            <td>{{ rec.commissioningDate || '—' }}</td>
            <td>
              <span :class="['state-badge', `state-${rec.maintenanceState.toLowerCase()}`]">
                {{ rec.maintenanceState }}
              </span>
            </td>
          </tr>
        </tbody>
      </table>

      <p v-else-if="preview && preview.records.length === 0" class="info">
        No in-scope records found. Check that the ERP has CIs with active maintenance plans.
      </p>
    </div>
  </div>
</template>

<style scoped>
.toolbar {
  display: flex;
  align-items: center;
  margin-bottom: 1.25rem;
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

/* Run card */
.run-card {
  border: 1px solid #e2e8f0;
  border-radius: 0.5rem;
  overflow: hidden;
  margin-bottom: 2rem;
}

.run-card-body {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1.5rem;
  padding: 1.25rem 1.5rem;
}

.run-info {
  flex: 1;
}

.run-desc {
  font-size: 0.875rem;
  color: #475569;
  margin: 0.4rem 0 0;
  line-height: 1.6;
  max-width: 560px;
}

.run-btn {
  flex-shrink: 0;
  padding: 0.5rem 1.5rem;
  background: #1a1a2e;
  color: #e2e8f0;
  border: none;
  border-radius: 0.375rem;
  font-size: 0.9rem;
  font-weight: 600;
  cursor: pointer;
  white-space: nowrap;
  align-self: center;
}

.run-btn:hover:not(:disabled) { background: #2d2d4e; }
.run-btn:disabled { opacity: 0.5; cursor: not-allowed; }

/* Result banners */
.result-banner {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0.75rem 1.5rem;
  font-size: 0.875rem;
  border-top: 1px solid transparent;
}

.result-ok {
  background: #f0fdf4;
  border-top-color: #bbf7d0;
  color: #166534;
}

.result-err {
  background: #fef2f2;
  border-top-color: #fecaca;
  color: #991b1b;
}

.result-icon {
  font-weight: 700;
  font-size: 1rem;
}

.result-text { flex: 1; }

.result-link {
  background: none;
  border: 1px solid currentColor;
  border-radius: 0.375rem;
  padding: 0.2rem 0.65rem;
  font-size: 0.8rem;
  font-weight: 600;
  cursor: pointer;
  color: inherit;
}

.result-link:hover { opacity: 0.75; }

/* Preview section */
.preview-section {
  margin-top: 0.5rem;
}

.preview-header {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-bottom: 0.4rem;
}

.preview-meta {
  font-size: 0.82rem;
  color: #64748b;
}

.refresh-btn {
  margin-left: auto;
  padding: 0.25rem 0.75rem;
  border: 1px solid #cbd5e1;
  border-radius: 0.375rem;
  background: #fff;
  font-size: 0.8rem;
  cursor: pointer;
  color: #475569;
}

.refresh-btn:disabled { opacity: 0.5; cursor: not-allowed; }

.preview-desc {
  font-size: 0.82rem;
  color: #64748b;
  margin: 0 0 0.75rem;
}

table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.85rem;
}

th, td {
  padding: 0.45rem 0.65rem;
  text-align: left;
  border-bottom: 1px solid #e2e8f0;
  vertical-align: middle;
}

th {
  background: #f8fafc;
  font-weight: 600;
  font-size: 0.72rem;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  white-space: nowrap;
}

tr:hover td { background: #f8fafc; }

.row-idx {
  color: #94a3b8;
  font-size: 0.78rem;
  text-align: center;
  width: 2rem;
}

.guid-cell {
  font-size: 0.75rem;
  color: #475569;
}

.state-badge {
  display: inline-block;
  padding: 0.12rem 0.45rem;
  border-radius: 9999px;
  font-size: 0.72rem;
  font-weight: 600;
}

.state-active    { background: #dcfce7; color: #166534; }
.state-inrepair  { background: #fef9c3; color: #854d0e; }
.state-decommissioned { background: #f1f5f9; color: #64748b; }

.info  { color: #64748b; }
.error { color: #dc2626; }
</style>
