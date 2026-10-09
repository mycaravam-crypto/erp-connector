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
import UsersSettings from '@/components/UsersSettings.vue'
import PermissionsSettings from '@/components/PermissionsSettings.vue'
import { useCurrentUser } from '@/composables/useCurrentUser'
import { Permission } from '@/lib/permissions'

// /settings: the settings forms (managed-export scheduler, GDPR denylist, branding) and this connector's
// instance identity — each only for a user holding its permission — plus Users and Permissions for Admins.
const { can, isAdmin } = useCurrentUser()
const show = {
  scheduler: can(Permission.SettingsScheduler),
  gdpr: can(Permission.SettingsGdpr),
  branding: can(Permission.SettingsBranding),
  instance: can(Permission.SettingsInstance),
}

const loading = ref(true)
const loadError = ref<string | null>(null)

const schedulerConfig = ref<SchedulerConfig | null>(null)
const gdprFields = ref<string[]>([])
const brandingConfig = ref<BrandingConfig | null>(null)
const instance = ref<ConnectorInstance | null>(null)

onMounted(async () => {
  try {
    const [cfg, gdpr, branding, connector] = await Promise.all([
      show.scheduler ? getSchedulerConfig() : null,
      show.gdpr ? getGdprDeniedFields() : null,
      show.branding ? getBranding() : null,
      show.instance ? getConnectorInstance() : null,
    ])
    schedulerConfig.value = cfg
    gdprFields.value = gdpr?.fields ?? []
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
        <p>
          <strong>Users</strong> and <strong>Permissions</strong> (Admins only) decide who can sign in and
          what users with the User role may see and do. Users only see the sections they're allowed to change.
        </p>
      </HelpTooltip>
    </template>
  </PageHeader>

  <div v-if="loading" class="text-text-secondary text-sm mt-4">Loading…</div>

  <Alert v-else-if="loadError" variant="danger" class="mt-4">{{ loadError }}</Alert>

  <!-- Every section but the first starts with its own divider, so none dangles whichever are hidden. -->
  <div v-else class="[&>*+*]:border-t [&>*+*]:border-border [&>*+*]:mt-10 [&>*+*]:pt-4">
    <SchedulerSettingsForm v-if="schedulerConfig" :config="schedulerConfig" />
    <GdprDenylistEditor v-if="show.gdpr" :initial-fields="gdprFields" />
    <BrandingSettingsForm v-if="brandingConfig" :config="brandingConfig" />
    <ConnectorInstanceInfo v-if="instance" v-model:instance="instance" />
    <UsersSettings v-if="isAdmin" />
    <PermissionsSettings v-if="isAdmin" />
  </div>
</template>
