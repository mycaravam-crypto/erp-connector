<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { getSchedulerConfig, getGdprDeniedFields, type SchedulerConfig } from '@/api/scheduler'
import Alert from '@/components/ui/Alert.vue'
import SchedulerSettingsForm from '@/components/SchedulerSettingsForm.vue'
import GdprDenylistEditor from '@/components/GdprDenylistEditor.vue'

const loading = ref(true)
const loadError = ref<string | null>(null)

const schedulerConfig = ref<SchedulerConfig | null>(null)
const gdprFields = ref<string[]>([])

onMounted(async () => {
  try {
    const [cfg, gdpr] = await Promise.all([getSchedulerConfig(), getGdprDeniedFields()])
    schedulerConfig.value = cfg
    gdprFields.value = gdpr.fields
  } catch {
    loadError.value = 'Could not load settings. Is the backend service running?'
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <div class="max-w-xl">
    <div class="flex items-center gap-3 mb-2">
      <h1 class="m-0 text-xl font-semibold text-text-primary">Settings</h1>
    </div>

    <div v-if="loading" class="text-text-secondary text-sm mt-4">Loading…</div>

    <Alert v-else-if="loadError" variant="danger" class="mt-4">{{ loadError }}</Alert>

    <template v-else-if="schedulerConfig">
      <SchedulerSettingsForm :config="schedulerConfig" />
      <GdprDenylistEditor :initial-fields="gdprFields" />
    </template>
  </div>
</template>
