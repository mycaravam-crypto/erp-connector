<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { getExport, deliverExport, type ExportDetail } from '@/api/exports'
import ReleaseDialog from '@/components/ReleaseDialog.vue'
import StatusBadge from '@/components/StatusBadge.vue'

const route = useRoute()
const router = useRouter()

const seqNo = computed(() => Number(route.params.seqNo))
const run = ref<ExportDetail | null>(null)
const loading = ref(true)
const notFound = ref(false)

// Delivery form state
const deliveryImportCount = ref<number | null>(null)
const deliveryNotes = ref('')
const delivering = ref(false)
const deliveryError = ref<string | null>(null)

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

async function submitDelivery() {
  if (!run.value) return
  delivering.value = true
  deliveryError.value = null
  const result = await deliverExport(run.value.sequenceNo, {
    importedRecordCount: deliveryImportCount.value,
    notes: deliveryNotes.value.trim() || null,
  })
  delivering.value = false
  if (result.ok) {
    await load()
  } else {
    deliveryError.value = result.message || `Error ${result.status}`
  }
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

      <!-- Sequence gap warning — shown before the release form so operators see it first -->
      <div v-if="run.sequenceGapWarning" class="gap-warning" role="alert">
        <strong>Sequence gap detected.</strong> {{ run.sequenceGapWarning }}
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
          <tr v-if="run.deliveredAt">
            <th>Delivered At (UTC)</th>
            <td>{{ run.deliveredAt }}</td>
          </tr>
          <tr v-if="run.deliveredBy">
            <th>Delivered By</th>
            <td>{{ run.deliveredBy }}</td>
          </tr>
          <tr v-if="run.importedRecordCount !== null">
            <th>Imported Records</th>
            <td>{{ run.importedRecordCount }}</td>
          </tr>
          <tr v-if="run.deliveryNotes">
            <th>Delivery Notes</th>
            <td>{{ run.deliveryNotes }}</td>
          </tr>
        </tbody>
      </table>

      <!-- Four-eyes release form (Pending runs only) -->
      <ReleaseDialog
        v-if="run.status === 'Pending'"
        :seqNo="run.sequenceNo"
        @released="load"
      />

      <!-- Delivery acknowledgement form (Released, not yet delivered) -->
      <div v-if="run.status === 'Released' && !run.deliveredAt" class="delivery-card">
        <h2>Record Physical Delivery</h2>
        <p class="hint">
          After handing the export file to the vendor, record the delivery here to close the
          custody chain. Import count is optional — enter it when the vendor confirms.
        </p>

        <div class="field">
          <label for="import-count">Vendor import count (optional)</label>
          <input
            id="import-count"
            type="number"
            min="0"
            v-model.number="deliveryImportCount"
            placeholder="e.g. 5"
          />
        </div>

        <div class="field">
          <label for="delivery-notes">Notes (optional)</label>
          <input
            id="delivery-notes"
            type="text"
            v-model="deliveryNotes"
            placeholder="e.g. USB-007, handed to J. Smith"
          />
        </div>

        <p v-if="deliveryError" class="error">{{ deliveryError }}</p>

        <button @click="submitDelivery" :disabled="delivering">
          {{ delivering ? 'Recording…' : 'Mark as Delivered' }}
        </button>
      </div>

      <!-- Delivery complete indicator -->
      <div v-if="run.deliveredAt" class="delivery-done">
        <span class="delivery-icon">✓</span>
        Delivered on {{ run.deliveredAt }} by {{ run.deliveredBy }}.
        <span v-if="run.importedRecordCount !== null">
          Vendor confirmed {{ run.importedRecordCount }} records imported.
        </span>
      </div>
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

h2 {
  margin: 0 0 0.25rem;
  font-size: 1.05rem;
}

/* Sequence gap warning */
.gap-warning {
  background: #fff7ed;
  border: 1px solid #fed7aa;
  border-left: 4px solid #f97316;
  border-radius: 0.375rem;
  padding: 0.75rem 1rem;
  font-size: 0.875rem;
  color: #92400e;
  margin-bottom: 1.25rem;
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

/* Delivery card */
.delivery-card {
  border: 1px solid #e2e8f0;
  border-radius: 0.5rem;
  padding: 1.25rem 1.5rem;
  max-width: 420px;
  margin-top: 1.5rem;
}

.hint {
  color: #64748b;
  font-size: 0.85rem;
  margin: 0 0 1rem;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  margin-bottom: 0.75rem;
}

label {
  font-size: 0.85rem;
  font-weight: 600;
}

input {
  padding: 0.4rem 0.6rem;
  border: 1px solid #cbd5e1;
  border-radius: 0.375rem;
  font-size: 0.9rem;
  outline: none;
}

input:focus {
  border-color: #6366f1;
  box-shadow: 0 0 0 2px #e0e7ff;
}

button {
  margin-top: 0.5rem;
  padding: 0.45rem 1.2rem;
  background: #4f46e5;
  color: #fff;
  border: none;
  border-radius: 0.375rem;
  font-size: 0.9rem;
  font-weight: 600;
  cursor: pointer;
}

button:disabled {
  opacity: 0.45;
  cursor: not-allowed;
}

/* Delivery done */
.delivery-done {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-top: 1.5rem;
  background: #f0fdf4;
  border: 1px solid #bbf7d0;
  border-radius: 0.375rem;
  padding: 0.65rem 1rem;
  font-size: 0.875rem;
  color: #166534;
  max-width: 500px;
}

.delivery-icon {
  font-weight: 700;
  font-size: 1rem;
}

.info  { color: #64748b; }
.error { color: #dc2626; font-size: 0.85rem; margin: 0.25rem 0; }
</style>
