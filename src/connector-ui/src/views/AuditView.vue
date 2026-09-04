<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { getAuditLog, type AuditEntry } from '@/api/audit'
import Button from '@/components/ui/Button.vue'
import Alert from '@/components/ui/Alert.vue'

const entries = ref<AuditEntry[]>([])
const loading = ref(true)
const loadError = ref<string | null>(null)

function formatDate(iso: string | null | undefined): string {
  if (!iso) return '—'
  // .NET "O" format emits +00:00 offset; only append Z for bare timestamps with no timezone.
  const hasTimezone = iso.endsWith('Z') || /[+-]\d{2}:\d{2}$/.test(iso)
  return new Date(hasTimezone ? iso : iso + 'Z').toLocaleString()
}

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

    <table v-else class="w-full text-sm border-collapse">
      <thead>
        <tr class="text-left text-text-secondary border-b border-border">
          <th class="px-3 py-2 font-semibold">Timestamp</th>
          <th class="px-3 py-2 font-semibold">User</th>
          <th class="px-3 py-2 font-semibold">Action</th>
          <th class="px-3 py-2 font-semibold">Detail</th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="entry in entries"
          :key="entry.id"
          class="border-b border-border hover:bg-surface-elevated"
        >
          <td class="px-3 py-2 whitespace-nowrap text-text-secondary">{{ formatDate(entry.timestamp) }}</td>
          <td class="px-3 py-2 font-medium text-text-primary">{{ entry.username }}</td>
          <td class="px-3 py-2 font-mono text-text-primary">{{ entry.action }}</td>
          <td class="px-3 py-2 text-text-secondary">{{ entry.detail ?? '—' }}</td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
