<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { getSchedulerConfig, getGdprDeniedFields, type SchedulerConfig } from '@/api/scheduler'
import Alert from '@/components/ui/Alert.vue'
import SchedulerSettingsForm from '@/components/SchedulerSettingsForm.vue'
import GdprDenylistEditor from '@/components/GdprDenylistEditor.vue'
import HelpTooltip from '@/components/ui/HelpTooltip.vue'

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
  <div class="max-w-5xl">
    <div class="flex items-center gap-3 mb-2">
      <h1 class="m-0 text-xl font-semibold text-text-primary">Settings</h1>
      <HelpTooltip label="What's on this page?" title="Two connector-wide settings">
        <p>
          <strong>Export Scheduler</strong> controls when the one managed CMDB export runs
          automatically. <strong>GDPR Denied Fields</strong> is a global list of field names blocked
          from every export, regardless of how any individual export is mapped.
        </p>
        <p>Export Jobs and Import Jobs have their own settings on their own edit pages, not here.</p>
      </HelpTooltip>
    </div>

    <div v-if="loading" class="text-text-secondary text-sm mt-4">Loading…</div>

    <Alert v-else-if="loadError" variant="danger" class="mt-4">{{ loadError }}</Alert>

    <template v-else-if="schedulerConfig">
      <SchedulerSettingsForm :config="schedulerConfig" />
      <GdprDenylistEditor :initial-fields="gdprFields" />
    </template>
  </div>
</template>
