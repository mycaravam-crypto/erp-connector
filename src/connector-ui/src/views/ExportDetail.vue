<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { getExport, type ExportDetail } from '@/api/exports'
import ReleaseDialog from '@/components/ReleaseDialog.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import RunDetailTable from '@/components/RunDetailTable.vue'
import SkipRunForm from '@/components/SkipRunForm.vue'
import DeliverRunForm from '@/components/DeliverRunForm.vue'
import { Check, ChevronLeft } from 'lucide-vue-next'
import Icon from '@/components/ui/Icon.vue'
import Button from '@/components/ui/Button.vue'
import Alert from '@/components/ui/Alert.vue'

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
    <Button variant="ghost" class="mb-4" @click="router.push({ name: 'exports' })">
      <template #icon><Icon :icon="ChevronLeft" :size="16" /></template>
      Back to list
    </Button>

    <p v-if="loading" class="text-text-secondary">Loading…</p>
    <p v-else-if="notFound" class="text-danger">Export run not found.</p>

    <template v-else-if="run">
      <div class="flex items-center gap-3 mb-4">
        <h1 class="m-0 text-xl font-semibold text-text-primary">Export Run #{{ run.sequenceNo }}</h1>
        <StatusBadge :status="run.status" />
      </div>

      <!-- Sequence gap warning -->
      <Alert v-if="run.sequenceGapWarning" variant="warning" title="Sequence gap detected." class="gap-warning mb-5">
        {{ run.sequenceGapWarning }}
      </Alert>

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
      <Alert v-if="run.deliveredAt" variant="success" class="delivery-done mt-6 max-w-lg">
        <template #icon><Icon :icon="Check" :size="20" /></template>
        Delivered on {{ formatDate(run.deliveredAt) }} by {{ run.deliveredBy }}.
        <span v-if="run.importedRecordCount !== null">
          Vendor confirmed {{ run.importedRecordCount }} records imported.
        </span>
      </Alert>
    </template>
  </div>
</template>
