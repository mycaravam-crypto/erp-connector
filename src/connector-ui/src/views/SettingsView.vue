<script setup lang="ts">
import { ref, onMounted } from 'vue'
import {
  getSchedulerConfig,
  saveSchedulerConfig,
  getGdprDeniedFields,
  saveGdprDeniedFields,
} from '@/api/scheduler'
import { Check, X } from 'lucide-vue-next'
import Icon from '@/components/ui/Icon.vue'
import Input from '@/components/ui/Input.vue'
import Select from '@/components/ui/Select.vue'
import Button from '@/components/ui/Button.vue'
import Alert from '@/components/ui/Alert.vue'

const loading = ref(true)
const loadError = ref<string | null>(null)

const scheduledTime = ref('06:00')
const retentionDays = ref(30)
const format = ref<'xlsx' | 'csv' | 'json'>('json')

const saving = ref(false)
const saveStatus = ref<'idle' | 'ok' | 'error'>('idle')
const saveMessage = ref('')

// GDPR denylist state
const deniedFields = ref<string[]>([])
const newField = ref('')
const gdprSaving = ref(false)
const gdprSaveStatus = ref<'idle' | 'ok' | 'error'>('idle')
const gdprSaveMessage = ref('')

onMounted(async () => {
  try {
    const [cfg, gdpr] = await Promise.all([getSchedulerConfig(), getGdprDeniedFields()])
    scheduledTime.value = cfg.scheduledTimeUtc
    retentionDays.value = cfg.retentionDays
    format.value = cfg.format
    deniedFields.value = [...gdpr.fields]
  } catch {
    loadError.value = 'Could not load settings. Is the backend service running?'
  } finally {
    loading.value = false
  }
})

async function save() {
  saving.value = true
  saveStatus.value = 'idle'
  saveMessage.value = ''
  try {
    const result = await saveSchedulerConfig({
      scheduledTimeUtc: scheduledTime.value,
      retentionDays: retentionDays.value,
      format: format.value,
    })
    if (result.ok) {
      saveStatus.value = 'ok'
      saveMessage.value = 'Settings saved. The new schedule takes effect on the next export cycle.'
    } else {
      saveStatus.value = 'error'
      saveMessage.value = result.error ?? 'Unknown error.'
    }
  } catch {
    saveStatus.value = 'error'
    saveMessage.value = 'Could not reach the backend. Is the backend service running?'
  } finally {
    saving.value = false
  }
}

function addField() {
  const f = newField.value.trim()
  if (f && !deniedFields.value.includes(f)) {
    deniedFields.value = [...deniedFields.value, f]
  }
  newField.value = ''
}

function removeField(field: string) {
  deniedFields.value = deniedFields.value.filter((f) => f !== field)
}

async function saveGdpr() {
  gdprSaving.value = true
  gdprSaveStatus.value = 'idle'
  gdprSaveMessage.value = ''
  try {
    const result = await saveGdprDeniedFields(deniedFields.value)
    if (result.ok) {
      gdprSaveStatus.value = 'ok'
      gdprSaveMessage.value = 'GDPR denylist saved. Changes take effect immediately.'
    } else {
      gdprSaveStatus.value = 'error'
      gdprSaveMessage.value = result.error ?? 'Unknown error.'
    }
  } catch {
    gdprSaveStatus.value = 'error'
    gdprSaveMessage.value = 'Could not reach the backend. Is the backend service running?'
  } finally {
    gdprSaving.value = false
  }
}
</script>

<template>
  <div class="max-w-xl">
    <div class="flex items-center gap-3 mb-2">
      <h1 class="m-0 text-xl font-semibold text-text-primary">Settings</h1>
    </div>

    <div v-if="loading" class="text-text-secondary text-sm mt-4">Loading…</div>

    <Alert v-else-if="loadError" variant="danger" class="mt-4">{{ loadError }}</Alert>

    <template v-else>
      <section class="mt-6">
        <h2 class="text-base font-semibold text-text-primary mb-1">Export Scheduler</h2>
        <p class="text-text-secondary text-sm mb-4 leading-relaxed">
          The scheduled export runs once daily at the configured UTC time.
          Changes take effect on the next export cycle — no restart required.
        </p>

        <form class="flex flex-col gap-4" @submit.prevent="save">
          <div class="flex gap-6">
            <Input id="scheduled-time" v-model="scheduledTime" type="time" label="Daily run time (UTC)" class="w-36" />

            <Input
              id="retention-days"
              v-model.number="retentionDays"
              type="number"
              :min="1"
              :max="3650"
              label="Retention (days)"
              help-text="1–3,650 days. Export runs and staging files older than this are deleted."
              class="w-28"
            />

            <Select id="scheduled-format" v-model="format" label="Export format" help-text="Nested JSON groups in the mapping only apply when set to JSON." class="w-28">
              <option value="xlsx">Excel</option>
              <option value="csv">CSV</option>
              <option value="json">JSON</option>
            </Select>
          </div>

          <div>
            <Button type="submit" variant="secondary" :loading="saving">
              {{ saving ? 'Saving…' : 'Save Settings' }}
            </Button>
          </div>
        </form>

        <Alert v-if="saveStatus === 'ok'" variant="success" class="mt-4">
          <template #icon><Icon :icon="Check" :size="16" /></template>
          {{ saveMessage }}
        </Alert>
        <Alert v-else-if="saveStatus === 'error'" variant="danger" class="mt-4">
          <template #icon><Icon :icon="X" :size="16" /></template>
          {{ saveMessage }}
        </Alert>
      </section>

      <section class="mt-10">
        <h2 class="text-base font-semibold text-text-primary mb-1">GDPR Denied Fields</h2>
        <p class="text-text-secondary text-sm mb-4 leading-relaxed">
          These fields are stripped from all exports at query time (GDPR Art. 5(1)(c)).
          Changes take effect immediately.
        </p>

        <div class="flex flex-wrap gap-2 mb-4">
          <span
            v-for="field in deniedFields"
            :key="field"
            class="inline-flex items-center gap-1 px-2.5 py-1 rounded-full bg-surface-elevated border border-border-strong text-text-primary text-xs font-medium"
          >
            {{ field }}
            <button
              type="button"
              class="ml-0.5 text-text-secondary hover:text-text-primary cursor-pointer leading-none bg-transparent border-none p-0"
              :aria-label="`Remove ${field}`"
              @click="removeField(field)"
            >
              <Icon :icon="X" :size="16" />
            </button>
          </span>
          <span v-if="deniedFields.length === 0" class="text-xs text-text-muted italic">No fields configured.</span>
        </div>

        <div class="flex gap-2 mb-4">
          <Input v-model="newField" placeholder="Field name to add…" class="w-56" @keydown.enter.prevent="addField" />
          <Button variant="secondary" @click="addField">Add</Button>
        </div>

        <Button variant="secondary" :loading="gdprSaving" @click="saveGdpr">
          {{ gdprSaving ? 'Saving…' : 'Save GDPR Denylist' }}
        </Button>

        <Alert v-if="gdprSaveStatus === 'ok'" variant="success" class="mt-4">
          <template #icon><Icon :icon="Check" :size="16" /></template>
          {{ gdprSaveMessage }}
        </Alert>
        <Alert v-else-if="gdprSaveStatus === 'error'" variant="danger" class="mt-4">
          <template #icon><Icon :icon="X" :size="16" /></template>
          {{ gdprSaveMessage }}
        </Alert>
      </section>
    </template>
  </div>
</template>
