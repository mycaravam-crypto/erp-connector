<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { getUsername } from '@/api/auth'
import { listUsers, updateUser, deleteUser, type UserAccount } from '@/api/users'
import AddUserForm from '@/components/AddUserForm.vue'
import ResetPasswordDialog from '@/components/ResetPasswordDialog.vue'
import Alert from '@/components/ui/Alert.vue'
import UserRowActions from '@/components/UserRowActions.vue'
import HelpTooltip from '@/components/ui/HelpTooltip.vue'
import { useToasts } from '@/composables/useToasts'
import { formatDate } from '@/lib/dates'

// Admin-only Settings → Users: every login with its role, plus adding a user, switching a role, resetting a
// password and deleting a user. The server refuses changes to your own role or account and to the last Admin.
const MIN_PASSWORD_LENGTH = 10
const toasts = useToasts()
const me = (getUsername() ?? '').toLowerCase()

const users = ref<UserAccount[]>([])
const loadError = ref<string | null>(null)

async function load() {
  try {
    users.value = await listUsers()
    loadError.value = null
  } catch {
    loadError.value = 'Could not load users.'
  }
}
onMounted(load)

const isMe = (u: UserAccount) => u.username.toLowerCase() === me

// ── Role ──────────────────────────────────────────────────────────────────────
const busyUser = ref<string | null>(null)

async function switchRole(u: UserAccount) {
  const role = u.role === 'Admin' ? 'User' : 'Admin'
  busyUser.value = u.username
  try {
    const result = await updateUser(u.username, { role })
    if (result.ok) {
      u.role = result.data.role
      toasts.success(`${u.username} is now ${role === 'Admin' ? 'an Admin' : 'a User'}.`)
    } else {
      toasts.error(result.error)
    }
  } finally {
    busyUser.value = null
  }
}

// ── Password reset (the user ResetPasswordDialog is open for) ─────────────────
const resetting = ref<string | null>(null)

// ── Delete ────────────────────────────────────────────────────────────────────
const confirmingDelete = ref<string | null>(null)

async function remove(u: UserAccount) {
  busyUser.value = u.username
  try {
    const result = await deleteUser(u.username)
    if (result.ok) {
      toasts.success(`User ${u.username} deleted.`)
      await load()
    } else {
      toasts.error(result.error)
    }
  } finally {
    busyUser.value = null
    confirmingDelete.value = null
  }
}
</script>

<template>
  <section class="mt-6">
    <span class="inline-flex items-center gap-1.5 mb-1">
      <h2 class="text-base font-semibold text-text-primary m-0">Users</h2>
      <HelpTooltip label="What can each role do?" title="Admins and Users">
        <p>
          <strong>Admins</strong> can see and do everything, including Connect, this list and the
          permissions below.
        </p>
        <p><strong>Users</strong> can see and do only what's ticked under <strong>Permissions</strong>.</p>
      </HelpTooltip>
    </span>
    <p class="text-text-secondary text-sm mb-4 leading-relaxed">
      Who can sign in, and as what. You can't change your own role or delete yourself, and there is always at
      least one Admin.
    </p>

    <Alert v-if="loadError" variant="danger" class="mb-4">{{ loadError }}</Alert>

    <table v-else class="w-full text-sm border-collapse mb-6">
      <thead>
        <tr class="text-left text-text-secondary border-b border-border">
          <th class="px-3 py-2 font-semibold">Username</th>
          <th class="px-3 py-2 font-semibold">Role</th>
          <th class="px-3 py-2 font-semibold">Created</th>
          <th class="px-3 py-2 font-semibold"></th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="u in users" :key="u.username" class="border-b border-border">
          <td class="px-3 py-2 font-medium text-text-primary">
            {{ u.username }}<span v-if="isMe(u)" class="text-text-muted font-normal"> (you)</span>
          </td>
          <td class="px-3 py-2 text-text-secondary">{{ u.role }}</td>
          <td class="px-3 py-2 text-text-secondary whitespace-nowrap">{{ formatDate(u.createdAt) }}</td>
          <td class="px-3 py-2 text-right whitespace-nowrap">
            <UserRowActions
              :user="u"
              :self="isMe(u)"
              :busy="busyUser === u.username"
              :confirming="confirmingDelete === u.username"
              @update:confirming="(v) => (confirmingDelete = v ? u.username : null)"
              @switch-role="switchRole(u)"
              @reset-password="resetting = u.username"
              @delete="remove(u)"
            />
          </td>
        </tr>
      </tbody>
    </table>

    <AddUserForm :min-password-length="MIN_PASSWORD_LENGTH" @added="load" />
    <ResetPasswordDialog v-model:username="resetting" :min-password-length="MIN_PASSWORD_LENGTH" />
  </section>
</template>
