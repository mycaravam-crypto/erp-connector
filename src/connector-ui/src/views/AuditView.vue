<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { getAuditLog, type AuditEntry } from '@/api/audit'
import Button from '@/components/ui/Button.vue'
import Alert from '@/components/ui/Alert.vue'
import AuditLogTable from '@/components/AuditLogTable.vue'
import HelpTooltip from '@/components/ui/HelpTooltip.vue'

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
  <div class="max-w-5xl">
    <div class="flex items-center justify-between gap-3 mb-4">
      <span class="inline-flex items-center gap-1.5">
        <h1 class="m-0 text-xl font-semibold text-text-primary">Audit Log</h1>
        <HelpTooltip label="What gets logged here?" title="What's in the audit log">
          <p>
            A record of who did what, and when — every save, release, delivery, skip, and rejection
            across exports and imports, kept for compliance and for tracing back "why does this row
            look like this."
          </p>
          <p>
            <strong>Example:</strong> if a purchase order's status looks wrong, scan this list for its
            import run's entry to see exactly who released it, who approved it, and what value it wrote.
          </p>
        </HelpTooltip>
      </span>
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
