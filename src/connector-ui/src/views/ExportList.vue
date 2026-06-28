<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { RouterLink } from 'vue-router'
import { listExports, type ExportSummary } from '@/api/exports'

const runs = ref<ExportSummary[]>([])
const error = ref<string | null>(null)
const loading = ref(true)

async function load() {
  loading.value = true
  error.value = null
  try {
    runs.value = await listExports()
  } catch {
    error.value = 'Could not reach the API. Is the backend running on :5000?'
  } finally {
    loading.value = false
  }
}

onMounted(load)

function statusClass(status: string) {
  return {
    badge: true,
    'badge-pending': status === 'Pending',
    'badge-released': status === 'Released',
    'badge-failed': status === 'Failed',
  }
}
</script>

<template>
  <div>
    <div class="toolbar">
      <h1>Export Runs</h1>
      <button @click="load" :disabled="loading">Refresh</button>
    </div>

    <p v-if="loading" class="info">Loading…</p>
    <p v-else-if="error" class="error">{{ error }}</p>
    <p v-else-if="runs.length === 0" class="info">No export runs yet.</p>

    <table v-else>
      <thead>
        <tr>
          <th>#</th>
          <th>Extracted At (UTC)</th>
          <th>Records</th>
          <th>SHA-256 (short)</th>
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
          <td><code>{{ run.sha256Short }}</code></td>
          <td><span :class="statusClass(run.status)">{{ run.status }}</span></td>
          <td>{{ run.dataFileName }}</td>
        </tr>
      </tbody>
    </table>
  </div>
</template>

<style scoped>
.toolbar {
  display: flex;
  align-items: center;
  gap: 1rem;
  margin-bottom: 1rem;
}

h1 {
  margin: 0;
  font-size: 1.25rem;
}

table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.9rem;
}

th, td {
  padding: 0.5rem 0.75rem;
  text-align: left;
  border-bottom: 1px solid #e2e8f0;
}

th {
  background: #f8fafc;
  font-weight: 600;
  font-size: 0.8rem;
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

tr:hover td {
  background: #f1f5f9;
}

.badge {
  display: inline-block;
  padding: 0.15rem 0.5rem;
  border-radius: 9999px;
  font-size: 0.75rem;
  font-weight: 600;
}

.badge-pending  { background: #fef9c3; color: #854d0e; }
.badge-released { background: #dcfce7; color: #166534; }
.badge-failed   { background: #fee2e2; color: #991b1b; }

.info  { color: #64748b; }
.error { color: #dc2626; }
</style>
