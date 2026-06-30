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
    <button
      class="bg-transparent border-0 text-indigo-600 text-sm cursor-pointer p-0 mb-4 hover:underline"
      @click="router.push({ name: 'exports' })"
    >← Back to list</button>

    <p v-if="loading" class="text-slate-500">Loading…</p>
    <p v-else-if="notFound" class="text-red-600">Export run not found.</p>

    <template v-else-if="run">
      <div class="flex items-center gap-3 mb-4">
        <h1 class="m-0 text-xl font-semibold">Export Run #{{ run.sequenceNo }}</h1>
        <StatusBadge :status="run.status" />
      </div>

      <!-- Sequence gap warning -->
      <div v-if="run.sequenceGapWarning" class="gap-warning bg-orange-50 border border-orange-200 border-l-4 border-l-orange-400 rounded-md px-4 py-3 text-sm text-orange-900 mb-5" role="alert">
        <strong>Sequence gap detected.</strong> {{ run.sequenceGapWarning }}
      </div>

      <table class="border-collapse text-sm max-w-xl">
        <tbody>
          <tr>
            <th class="px-3 py-2 text-left font-semibold text-slate-500 w-40 whitespace-nowrap border-b border-slate-200 align-top">Sequence No</th>
            <td class="px-3 py-2 border-b border-slate-200 align-top">{{ run.sequenceNo }}</td>
          </tr>
          <tr>
            <th class="px-3 py-2 text-left font-semibold text-slate-500 w-40 whitespace-nowrap border-b border-slate-200 align-top">Extracted At (UTC)</th>
            <td class="px-3 py-2 border-b border-slate-200 align-top">{{ run.extractedAt }}</td>
          </tr>
          <tr>
            <th class="px-3 py-2 text-left font-semibold text-slate-500 w-40 whitespace-nowrap border-b border-slate-200 align-top">Record Count</th>
            <td class="px-3 py-2 border-b border-slate-200 align-top">{{ run.recordCount }}</td>
          </tr>
          <tr>
            <th class="px-3 py-2 text-left font-semibold text-slate-500 w-40 whitespace-nowrap border-b border-slate-200 align-top">SHA-256</th>
            <td class="px-3 py-2 border-b border-slate-200 align-top"><code class="text-xs break-all">{{ run.sha256 }}</code></td>
          </tr>
          <tr>
            <th class="px-3 py-2 text-left font-semibold text-slate-500 w-40 whitespace-nowrap border-b border-slate-200 align-top">File</th>
            <td class="px-3 py-2 border-b border-slate-200 align-top">{{ run.dataFileName }}</td>
          </tr>
          <tr v-if="run.releasedAt">
            <th class="px-3 py-2 text-left font-semibold text-slate-500 w-40 whitespace-nowrap border-b border-slate-200 align-top">Released At (UTC)</th>
            <td class="px-3 py-2 border-b border-slate-200 align-top">{{ run.releasedAt }}</td>
          </tr>
          <tr v-if="run.operatedBy">
            <th class="px-3 py-2 text-left font-semibold text-slate-500 w-40 whitespace-nowrap border-b border-slate-200 align-top">Operated By</th>
            <td class="px-3 py-2 border-b border-slate-200 align-top">{{ run.operatedBy }}</td>
          </tr>
          <tr v-if="run.approvedBy">
            <th class="px-3 py-2 text-left font-semibold text-slate-500 w-40 whitespace-nowrap border-b border-slate-200 align-top">Approved By</th>
            <td class="px-3 py-2 border-b border-slate-200 align-top">{{ run.approvedBy }}</td>
          </tr>
          <tr v-if="run.deliveredAt">
            <th class="px-3 py-2 text-left font-semibold text-slate-500 w-40 whitespace-nowrap border-b border-slate-200 align-top">Delivered At (UTC)</th>
            <td class="px-3 py-2 border-b border-slate-200 align-top">{{ run.deliveredAt }}</td>
          </tr>
          <tr v-if="run.deliveredBy">
            <th class="px-3 py-2 text-left font-semibold text-slate-500 w-40 whitespace-nowrap border-b border-slate-200 align-top">Delivered By</th>
            <td class="px-3 py-2 border-b border-slate-200 align-top">{{ run.deliveredBy }}</td>
          </tr>
          <tr v-if="run.importedRecordCount !== null">
            <th class="px-3 py-2 text-left font-semibold text-slate-500 w-40 whitespace-nowrap border-b border-slate-200 align-top">Imported Records</th>
            <td class="px-3 py-2 border-b border-slate-200 align-top">{{ run.importedRecordCount }}</td>
          </tr>
          <tr v-if="run.deliveryNotes">
            <th class="px-3 py-2 text-left font-semibold text-slate-500 w-40 whitespace-nowrap border-b border-slate-200 align-top">Delivery Notes</th>
            <td class="px-3 py-2 border-b border-slate-200 align-top">{{ run.deliveryNotes }}</td>
          </tr>
        </tbody>
      </table>

      <!-- Four-eyes release form -->
      <ReleaseDialog
        v-if="run.status === 'Pending'"
        :seqNo="run.sequenceNo"
        @released="load"
      />

      <!-- Delivery acknowledgement form -->
      <div v-if="run.status === 'Released' && !run.deliveredAt" class="delivery-card border border-slate-200 rounded-lg px-6 py-5 max-w-sm mt-6">
        <h2 class="m-0 mb-1 text-base font-semibold">Record Physical Delivery</h2>
        <p class="text-slate-500 text-sm m-0 mb-4 leading-relaxed">
          After handing the export file to the vendor, record the delivery here to close the
          custody chain. Import count is optional — enter it when the vendor confirms.
        </p>

        <div class="flex flex-col gap-1 mb-3">
          <label for="import-count" class="text-sm font-semibold">Vendor import count (optional)</label>
          <input
            id="import-count"
            type="number"
            min="0"
            v-model.number="deliveryImportCount"
            placeholder="e.g. 5"
            class="px-2.5 py-1.5 border border-slate-300 rounded-md text-sm outline-none focus:border-indigo-500 focus:ring-2 focus:ring-indigo-100"
          />
        </div>

        <div class="flex flex-col gap-1 mb-3">
          <label for="delivery-notes" class="text-sm font-semibold">Notes (optional)</label>
          <input
            id="delivery-notes"
            type="text"
            v-model="deliveryNotes"
            placeholder="e.g. USB-007, handed to J. Smith"
            class="px-2.5 py-1.5 border border-slate-300 rounded-md text-sm outline-none focus:border-indigo-500 focus:ring-2 focus:ring-indigo-100"
          />
        </div>

        <p v-if="deliveryError" class="text-red-600 text-sm mb-2">{{ deliveryError }}</p>

        <button
          class="mt-1 px-5 py-2 bg-indigo-600 text-white border-0 rounded-md text-sm font-semibold cursor-pointer disabled:opacity-45 disabled:cursor-not-allowed hover:enabled:bg-indigo-700"
          :disabled="delivering"
          @click="submitDelivery"
        >{{ delivering ? 'Recording…' : 'Mark as Delivered' }}</button>
      </div>

      <!-- Delivery complete indicator -->
      <div v-if="run.deliveredAt" class="delivery-done flex items-center gap-2 mt-6 bg-green-50 border border-green-200 rounded-md px-4 py-2.5 text-sm text-green-800 max-w-lg">
        <span class="font-bold text-base">✓</span>
        Delivered on {{ run.deliveredAt }} by {{ run.deliveredBy }}.
        <span v-if="run.importedRecordCount !== null">
          Vendor confirmed {{ run.importedRecordCount }} records imported.
        </span>
      </div>
    </template>
  </div>
</template>
