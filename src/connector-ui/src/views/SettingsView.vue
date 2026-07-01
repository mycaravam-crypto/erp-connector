<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { getSchedulerConfig, saveSchedulerConfig } from '@/api/scheduler'
import { getGdprDeniedFields, saveGdprDeniedFields } from '@/api/audit'

const loading = ref(true)
const loadError = ref<string | null>(null)

const scheduledTime = ref('06:00')
const retentionDays = ref(30)

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
      <h1 class="m-0 text-xl font-semibold">Settings</h1>
    </div>

    <div v-if="loading" class="text-slate-500 text-sm mt-4">Loading…</div>

    <div v-else-if="loadError" class="bg-red-50 border border-red-200 rounded-lg px-4 py-3 text-sm text-red-800 mt-4">
      {{ loadError }}
    </div>

    <template v-else>
      <section class="mt-6">
        <h2 class="text-base font-semibold text-slate-800 mb-1">Export Scheduler</h2>
        <p class="text-slate-500 text-sm mb-4 leading-relaxed">
          The scheduled export runs once daily at the configured UTC time.
          Changes take effect on the next export cycle — no restart required.
        </p>

        <form class="flex flex-col gap-4" @submit.prevent="save">
          <div class="flex gap-6">
            <div class="flex flex-col gap-1">
              <label for="scheduled-time" class="text-xs font-semibold text-slate-700">
                Daily run time (UTC)
              </label>
              <input
                id="scheduled-time"
                v-model="scheduledTime"
                type="time"
                class="px-2.5 py-2 border border-slate-300 rounded-md text-sm text-slate-900 bg-white outline-none focus:outline-indigo-600 focus:outline-2 focus:border-transparent w-36"
              />
            </div>

            <div class="flex flex-col gap-1">
              <label for="retention-days" class="text-xs font-semibold text-slate-700">
                Retention (days)
              </label>
              <input
                id="retention-days"
                v-model.number="retentionDays"
                type="number"
                min="1"
                max="3650"
                class="px-2.5 py-2 border border-slate-300 rounded-md text-sm text-slate-900 bg-white outline-none focus:outline-indigo-600 focus:outline-2 focus:border-transparent w-28"
              />
              <p class="text-xs text-slate-400 mt-0.5">1–3,650 days. Export runs and staging files older than this are deleted.</p>
            </div>
          </div>

          <div>
            <button
              type="submit"
              class="px-5 py-2 border border-slate-400 rounded-md bg-white text-slate-900 text-sm font-semibold cursor-pointer hover:enabled:bg-slate-50 disabled:opacity-50 disabled:cursor-not-allowed"
              :disabled="saving"
            >
              {{ saving ? 'Saving…' : 'Save Settings' }}
            </button>
          </div>
        </form>

        <div
          v-if="saveStatus === 'ok'"
          class="flex items-center gap-2 mt-4 px-4 py-3 rounded-md bg-green-50 border border-green-200 text-green-800 text-sm"
        >
          <span class="font-bold">✓</span>
          {{ saveMessage }}
        </div>
        <div
          v-else-if="saveStatus === 'error'"
          class="flex items-center gap-2 mt-4 px-4 py-3 rounded-md bg-red-50 border border-red-200 text-red-800 text-sm"
        >
          <span class="font-bold">✕</span>
          {{ saveMessage }}
        </div>
      </section>

      <section class="mt-10">
        <h2 class="text-base font-semibold text-slate-800 mb-1">GDPR Denied Fields</h2>
        <p class="text-slate-500 text-sm mb-4 leading-relaxed">
          These fields are stripped from all exports at query time (GDPR Art. 5(1)(c)).
          Changes take effect immediately.
        </p>

        <div class="flex flex-wrap gap-2 mb-4">
          <span
            v-for="field in deniedFields"
            :key="field"
            class="inline-flex items-center gap-1 px-2.5 py-1 rounded-full bg-slate-100 border border-slate-300 text-slate-800 text-xs font-medium"
          >
            {{ field }}
            <button
              type="button"
              class="ml-0.5 text-slate-500 hover:text-slate-800 cursor-pointer leading-none bg-transparent border-none p-0"
              :aria-label="`Remove ${field}`"
              @click="removeField(field)"
            >
              &times;
            </button>
          </span>
          <span v-if="deniedFields.length === 0" class="text-xs text-slate-400 italic">No fields configured.</span>
        </div>

        <div class="flex gap-2 mb-4">
          <input
            v-model="newField"
            type="text"
            placeholder="Field name to add…"
            class="px-2.5 py-2 border border-slate-300 rounded-md text-sm text-slate-900 bg-white outline-none focus:outline-indigo-600 focus:outline-2 focus:border-transparent w-56"
            @keydown.enter.prevent="addField"
          />
          <button
            type="button"
            class="px-4 py-2 border border-slate-400 rounded-md bg-white text-slate-900 text-sm font-semibold cursor-pointer hover:bg-slate-50"
            @click="addField"
          >
            Add
          </button>
        </div>

        <button
          type="button"
          class="px-5 py-2 border border-slate-400 rounded-md bg-white text-slate-900 text-sm font-semibold cursor-pointer hover:enabled:bg-slate-50 disabled:opacity-50 disabled:cursor-not-allowed"
          :disabled="gdprSaving"
          @click="saveGdpr"
        >
          {{ gdprSaving ? 'Saving…' : 'Save GDPR Denylist' }}
        </button>

        <div
          v-if="gdprSaveStatus === 'ok'"
          class="flex items-center gap-2 mt-4 px-4 py-3 rounded-md bg-green-50 border border-green-200 text-green-800 text-sm"
        >
          <span class="font-bold">&#10003;</span>
          {{ gdprSaveMessage }}
        </div>
        <div
          v-else-if="gdprSaveStatus === 'error'"
          class="flex items-center gap-2 mt-4 px-4 py-3 rounded-md bg-red-50 border border-red-200 text-red-800 text-sm"
        >
          <span class="font-bold">&#10005;</span>
          {{ gdprSaveMessage }}
        </div>
      </section>
    </template>
  </div>
</template>
