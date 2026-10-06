<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { getSchedulerConfig, getGdprDeniedFields, type SchedulerConfig } from '@/api/scheduler'
import { getBranding, type BrandingConfig } from '@/api/branding'
import { getConnectorInstance, type ConnectorInstance } from '@/api/instance'
import Alert from '@/components/ui/Alert.vue'
import SchedulerSettingsForm from '@/components/SchedulerSettingsForm.vue'
import GdprDenylistEditor from '@/components/GdprDenylistEditor.vue'
import BrandingSettingsForm from '@/components/BrandingSettingsForm.vue'
import ConnectorInstanceInfo from '@/components/ConnectorInstanceInfo.vue'
import HelpTooltip from '@/components/ui/HelpTooltip.vue'
import PageHeader from '@/components/ui/PageHeader.vue'

// /settings: loads and shows the three settings forms (managed-export scheduler, GDPR denylist, branding) and
// this connector's instance identity.
const loading = ref(true)
const loadError = ref<string | null>(null)

const schedulerConfig = ref<SchedulerConfig | null>(null)
const gdprFields = ref<string[]>([])
const brandingConfig = ref<BrandingConfig | null>(null)
const instance = ref<ConnectorInstance | null>(null)

onMounted(async () => {
  try {
    const [cfg, gdpr, branding, connector] = await Promise.all([
      getSchedulerConfig(),
      getGdprDeniedFields(),
      getBranding(),
      getConnectorInstance(),
    ])
    schedulerConfig.value = cfg
    gdprFields.value = gdpr.fields
    brandingConfig.value = branding
    instance.value = connector
  } catch {
    loadError.value = 'Could not load settings. Is the backend service running?'
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <PageHeader title="Settings">
    <template #help>
      <HelpTooltip label="What's on this page?" title="Two connector-wide settings">
        <p>
          <strong>Export Scheduler</strong> controls when the one managed CMDB export runs
          automatically. <strong>GDPR Denied Fields</strong> is a global list of field names blocked
          from every export, regardless of how any individual export is mapped.
        </p>
        <p>Export Jobs and Import Jobs have their own settings on their own edit pages, not here.</p>
        <p>
          <strong>Branding</strong> lets you replace the default name, logo, favicon, and background
          with your own, applied everywhere in the UI.
        </p>
        <p>
          <strong>Connector Instance</strong> shows this installation's ID, which identifies its exports
          to other connector instances, and can regenerate it for a copy of another installation.
        </p>
      </HelpTooltip>
    </template>
  </PageHeader>

  <div v-if="loading" class="text-text-secondary text-sm mt-4">Loading…</div>

  <Alert v-else-if="loadError" variant="danger" class="mt-4">{{ loadError }}</Alert>

  <template v-else-if="schedulerConfig && brandingConfig && instance">
    <SchedulerSettingsForm :config="schedulerConfig" />
    <hr class="my-10 border-border" />
    <GdprDenylistEditor :initial-fields="gdprFields" />
    <hr class="my-10 border-border" />
    <BrandingSettingsForm :config="brandingConfig" />
    <hr class="my-10 border-border" />
    <ConnectorInstanceInfo v-model:instance="instance" />
  </template>
</template>
