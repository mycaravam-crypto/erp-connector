<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { getExport, type ExportDetail } from '@/api/exports'
import ReleaseDialog from '@/components/ReleaseDialog.vue'
import StatusBadge from '@/components/StatusBadge.vue'

const route = useRoute()
const router = useRouter()

const seqNo = computed(() => Number(route.params.seqNo))
const run = ref<ExportDetail | null>(null)
const loading = ref(true)
const notFound = ref(false)

async function load() {
  loading.value = true
  notFound.value = false
  const result = await getExport(seqNo.value)
  if (result === null) {
    notFound.value = true
  } else {
    run.value = result
  }
  loading.value = false
}

onMounted(load)
</script>

<template>
  <div>
    <button class="back-btn" @click="router.push({ name: 'exports' })">← Back to list</button>

    <p v-if="loading" class="info">Loading…</p>
    <p v-else-if="notFound" class="error">Export run not found.</p>

    <template v-else-if="run">
      <div class="header-row">
        <h1>Export Run #{{ run.sequenceNo }}</h1>
        <StatusBadge :status="run.status" />
      </div>

      <table class="detail-table">
        <tbody>
          <tr>
            <th>Sequence No</th>
            <td>{{ run.sequenceNo }}</td>
          </tr>
          <tr>
            <th>Extracted At (UTC)</th>
            <td>{{ run.extractedAt }}</td>
          </tr>
          <tr>
            <th>Record Count</th>
            <td>{{ run.recordCount }}</td>
          </tr>
          <tr>
            <th>SHA-256</th>
            <td><code class="sha">{{ run.sha256 }}</code></td>
          </tr>
          <tr>
            <th>File</th>
            <td>{{ run.dataFileName }}</td>
          </tr>
          <tr v-if="run.releasedAt">
            <th>Released At (UTC)</th>
            <td>{{ run.releasedAt }}</td>
          </tr>
          <tr v-if="run.operatedBy">
            <th>Operated By</th>
            <td>{{ run.operatedBy }}</td>
          </tr>
          <tr v-if="run.approvedBy">
            <th>Approved By</th>
            <td>{{ run.approvedBy }}</td>
          </tr>
        </tbody>
      </table>

      <ReleaseDialog
        v-if="run.status === 'Pending'"
        :seqNo="run.sequenceNo"
        @released="load"
      />
    </template>
  </div>
</template>

<style scoped>
.back-btn {
  background: none;
  border: none;
  color: #4f46e5;
  font-size: 0.9rem;
  cursor: pointer;
  padding: 0;
  margin-bottom: 1rem;
}

.back-btn:hover {
  text-decoration: underline;
}

.header-row {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-bottom: 1rem;
}

h1 {
  margin: 0;
  font-size: 1.25rem;
}

.detail-table {
  border-collapse: collapse;
  font-size: 0.9rem;
  max-width: 640px;
}

.detail-table th, .detail-table td {
  padding: 0.45rem 0.75rem;
  text-align: left;
  border-bottom: 1px solid #e2e8f0;
  vertical-align: top;
}

.detail-table th {
  font-weight: 600;
  color: #475569;
  width: 160px;
  white-space: nowrap;
}

.sha {
  font-size: 0.8rem;
  word-break: break-all;
}

.info  { color: #64748b; }
.error { color: #dc2626; }
</style>
