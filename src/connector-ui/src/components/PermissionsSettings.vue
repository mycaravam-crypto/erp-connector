<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { getRolePermissions, saveRolePermissions, type PermissionGroup } from '@/api/permissions'
import { listUsers } from '@/api/users'
import Button from '@/components/ui/Button.vue'
import Alert from '@/components/ui/Alert.vue'
import Card from '@/components/ui/Card.vue'
import HelpTooltip from '@/components/ui/HelpTooltip.vue'
import SaveStatusAlert from '@/components/ui/SaveStatusAlert.vue'
import { useToasts } from '@/composables/useToasts'
import { useSaveStatus } from '@/composables/useSaveStatus'
import { Permission } from '@/lib/permissions'

// Admin-only Settings → Permissions: one card per menu item with a checkbox per permission, deciding what the
// User role may see and do. Ticking any permission ticks the item's "View" too, and unticking "View" clears the
// rest, mirroring how the server normalizes the set. Applies on the users' next click after saving.
const toasts = useToasts()

const catalogue = ref<PermissionGroup[]>([])
const granted = ref(new Set<string>())
const adminCount = ref(0)
const userCount = ref(0)
const loadError = ref<string | null>(null)
const { saving, saveStatus, saveMessage, reset: resetSaveStatus } = useSaveStatus()

onMounted(async () => {
  try {
    const [permissions, users] = await Promise.all([getRolePermissions(), listUsers()])
    catalogue.value = permissions.catalogue
    granted.value = new Set(permissions.userPermissions)
    adminCount.value = users.filter((u) => u.role === 'Admin').length
    userCount.value = users.length - adminCount.value
  } catch {
    loadError.value = 'Could not load permissions.'
  }
})

const viewOf = (group: PermissionGroup) => group.permissions.find((p) => p.key.endsWith('.view'))?.key

function toggle(group: PermissionGroup, key: string, on: boolean) {
  const next = new Set(granted.value)
  const view = viewOf(group)
  if (on) {
    next.add(key)
    if (view) next.add(view)
  } else if (key === view) {
    for (const p of group.permissions) next.delete(p.key)
  } else {
    next.delete(key)
  }
  granted.value = next
  resetSaveStatus()
}

function setGroup(group: PermissionGroup, on: boolean) {
  const next = new Set(granted.value)
  for (const p of group.permissions) {
    if (on) next.add(p.key)
    else next.delete(p.key)
  }
  granted.value = next
  resetSaveStatus()
}

// Four-eyes needs two different people allowed to release. With fewer than two Admins and the User role unable
// to release, nothing of that kind could ever be released.
const releaseWarnings = computed(() =>
  [
    { key: Permission.ManagedExportRelease, label: 'Managed Export runs' },
    { key: Permission.ImportJobsRelease, label: 'staged imports' },
  ]
    .filter(({ key }) => adminCount.value + (granted.value.has(key) ? userCount.value : 0) < 2)
    .map(({ label }) => label),
)

async function save() {
  saving.value = true
  resetSaveStatus()
  const result = await saveRolePermissions([...granted.value]).catch(() => ({
    ok: false as const,
    error: 'Could not reach the backend. Is the backend service running?',
  }))
  saving.value = false
  if (result.ok) granted.value = new Set(result.data.userPermissions)
  saveStatus.value = result.ok ? 'ok' : 'error'
  saveMessage.value = result.ok ? 'Permissions saved. Users see the change on their next click.' : result.error
  if (result.ok) toasts.success(saveMessage.value)
  else toasts.error(saveMessage.value)
}
</script>

<template>
  <section class="mt-6">
    <span class="inline-flex items-center gap-1.5 mb-1">
      <h2 class="text-base font-semibold text-text-primary m-0">Permissions</h2>
      <HelpTooltip label="How do permissions work?" title="What Users may see and do">
        <p>
          Each card is one menu item. Without <strong>View</strong> the item is hidden for Users, and every
          other permission needs it, so ticking one ticks View too.
        </p>
        <p>Admins always have every permission. Connect, Users and Permissions are Admin-only.</p>
        <p>The server checks these on every request; hiding a button is only a convenience.</p>
      </HelpTooltip>
    </span>
    <p class="text-text-secondary text-sm mb-4 leading-relaxed">What users with the User role may see and do.</p>

    <Alert v-if="loadError" variant="danger">{{ loadError }}</Alert>

    <template v-else-if="catalogue.length > 0">
      <Alert v-if="releaseWarnings.length > 0" variant="warning" class="mb-4">
        Nobody can release {{ releaseWarnings.join(' or ') }}: four-eyes release needs two different people
        allowed to release, and there {{ adminCount === 1 ? 'is only 1 Admin' : `are ${adminCount} Admins` }}.
        Grant Users the release permission or add another Admin.
      </Alert>

      <div class="grid gap-3 sm:grid-cols-2">
        <Card v-for="group in catalogue" :key="group.key">
          <div class="flex items-center justify-between gap-2 mb-2">
            <h3 class="m-0 text-sm font-semibold text-text-primary">{{ group.label }}</h3>
            <span class="flex gap-2 text-xs">
              <button
                type="button"
                class="text-brand bg-transparent border-0 p-0 cursor-pointer hover:underline"
                @click="setGroup(group, true)"
              >All</button>
              <button
                type="button"
                class="text-text-secondary bg-transparent border-0 p-0 cursor-pointer hover:underline"
                @click="setGroup(group, false)"
              >None</button>
            </span>
          </div>
          <label
            v-for="p in group.permissions"
            :key="p.key"
            class="flex items-start gap-2 py-1 text-sm cursor-pointer"
          >
            <input
              type="checkbox"
              class="mt-0.5 cursor-pointer"
              :checked="granted.has(p.key)"
              @change="toggle(group, p.key, ($event.target as HTMLInputElement).checked)"
            />
            <span>
              <span class="text-text-primary">{{ p.label }}</span>
              <span class="block text-xs text-text-secondary">{{ p.description }}</span>
            </span>
          </label>
        </Card>
      </div>

      <div class="mt-4">
        <Button variant="secondary" :loading="saving" @click="save">
          {{ saving ? 'Saving…' : 'Save Permissions' }}
        </Button>
      </div>
      <SaveStatusAlert :status="saveStatus" :message="saveMessage" />
    </template>
  </section>
</template>
