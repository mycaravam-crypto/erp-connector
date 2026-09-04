<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { getExport, type ExportDetail } from '@/api/exports'
import ReleaseDialog from '@/components/ReleaseDialog.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import RunDetailTable from '@/components/RunDetailTable.vue'
import SkipRunForm from '@/components/SkipRunForm.vue'
import DeliverRunForm from '@/components/DeliverRunForm.vue'
import { Check } from 'lucide-vue-next'
import Icon from '@/components/ui/Icon.vue'

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

function formatDate(iso: string | null | undefined): string {
  if (!iso) return '—'
  return new Date(iso.includes('Z') ? iso : iso + 'Z').toLocaleString()
}
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

      <RunDetailTable :run="run" />

      <!-- Four-eyes release form -->
      <ReleaseDialog
        v-if="run.status === 'Pending'"
        :seqNo="run.sequenceNo"
        @released="load"
      />

      <!-- Skip run form (Pending or Failed) -->
      <SkipRunForm
        v-if="run.status === 'Pending' || run.status === 'Failed'"
        :seqNo="run.sequenceNo"
        @skipped="load"
      />

      <!-- Delivery acknowledgement form -->
      <DeliverRunForm
        v-if="run.status === 'Released' && !run.deliveredAt"
        :seqNo="run.sequenceNo"
        @delivered="load"
      />

      <!-- Delivery complete indicator -->
      <div v-if="run.deliveredAt" class="delivery-done flex items-center gap-2 mt-6 bg-green-50 border border-green-200 rounded-md px-4 py-2.5 text-sm text-green-800 max-w-lg">
        <Icon :icon="Check" :size="20" />
        Delivered on {{ formatDate(run.deliveredAt) }} by {{ run.deliveredBy }}.
        <span v-if="run.importedRecordCount !== null">
          Vendor confirmed {{ run.importedRecordCount }} records imported.
        </span>
      </div>
    </template>
  </div>
</template>
