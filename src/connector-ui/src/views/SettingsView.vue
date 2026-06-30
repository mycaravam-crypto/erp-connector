<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { getSchedulerConfig, saveSchedulerConfig } from '@/api/scheduler'

const loading = ref(true)
const loadError = ref<string | null>(null)

const scheduledTime = ref('06:00')
const retentionDays = ref(30)

const saving = ref(false)
const saveStatus = ref<'idle' | 'ok' | 'error'>('idle')
const saveMessage = ref('')

onMounted(async () => {
  try {
    const cfg = await getSchedulerConfig()
    scheduledTime.value = cfg.scheduledTimeUtc
    retentionDays.value = cfg.retentionDays
  } catch {
    loadError.value = 'Could not load scheduler settings. Is the backend service running?'
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
      saveMessage.value = result.error
    }
  } catch {
    saveStatus.value = 'error'
    saveMessage.value = 'Could not reach the backend. Is the backend service running?'
  } finally {
    saving.value = false
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
              <p class="text-xs text-slate-400 mt-0.5">Export runs and staging files older than this are deleted.</p>
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
    </template>
  </div>
</template>
