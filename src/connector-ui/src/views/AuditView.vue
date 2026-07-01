<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { getAuditLog, type AuditEntry } from '@/api/audit'

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
      <h1 class="m-0 text-xl font-semibold">Audit Log</h1>
      <button
        type="button"
        class="px-4 py-1.5 border border-slate-400 rounded-md bg-white text-slate-900 text-sm font-semibold cursor-pointer hover:bg-slate-50 disabled:opacity-50"
        :disabled="loading"
        @click="load"
      >
        {{ loading ? 'Loading…' : 'Refresh' }}
      </button>
    </div>

    <div v-if="loading && entries.length === 0" class="text-slate-500 text-sm mt-4">Loading…</div>

    <div
      v-else-if="loadError"
      class="bg-red-50 border border-red-200 rounded-lg px-4 py-3 text-sm text-red-800 mt-4"
    >
      {{ loadError }}
    </div>

    <div v-else-if="entries.length === 0" class="text-slate-500 text-sm mt-4">
      No audit entries yet.
    </div>

    <table v-else class="w-full text-sm border-collapse">
      <thead>
        <tr class="text-left text-slate-600 border-b border-slate-200">
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
          class="border-b border-slate-100 hover:bg-slate-50"
        >
          <td class="px-3 py-2 whitespace-nowrap text-slate-600">{{ formatDate(entry.timestamp) }}</td>
          <td class="px-3 py-2 font-medium text-slate-800">{{ entry.username }}</td>
          <td class="px-3 py-2 font-mono text-slate-700">{{ entry.action }}</td>
          <td class="px-3 py-2 text-slate-500">{{ entry.detail ?? '—' }}</td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
