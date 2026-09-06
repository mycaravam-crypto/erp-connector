<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { getAuditLog, type AuditEntry } from '@/api/audit'
import Button from '@/components/ui/Button.vue'
import Alert from '@/components/ui/Alert.vue'
import AuditLogTable from '@/components/AuditLogTable.vue'

const entries = ref<AuditEntry[]>([])
const loading = ref(true)
const loadError = ref<string | null>(null)

async function load() {
  loading.value = true
  loadError.value = null
  try {
    entries.value = await getAuditLog(200)
  } catch {
    loadError.value = 'Could not load audit log. Is the backend service running?'
  } finally {
    loading.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="max-w-4xl">
    <div class="flex items-center justify-between gap-3 mb-4">
      <h1 class="m-0 text-xl font-semibold text-text-primary">Audit Log</h1>
      <Button variant="secondary" :loading="loading" @click="load">
        {{ loading ? 'Loading…' : 'Refresh' }}
      </Button>
    </div>

    <div v-if="loading && entries.length === 0" class="text-text-secondary text-sm mt-4">Loading…</div>

    <Alert v-else-if="loadError" variant="danger" class="mt-4">{{ loadError }}</Alert>

    <div v-else-if="entries.length === 0" class="text-text-secondary text-sm mt-4">
      No audit entries yet.
    </div>

    <AuditLogTable v-else :entries="entries" />
  </div>
</template>
