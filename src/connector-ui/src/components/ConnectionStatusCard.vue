<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { RouterLink } from 'vue-router'
import { Plug } from 'lucide-vue-next'
import { getConnection, getConnectionStatus, type ConnectionStatus, type ErpConnectionInfo } from '@/api/connection'
import Icon from '@/components/ui/Icon.vue'
import Card from '@/components/ui/Card.vue'
import { useCurrentUser } from '@/composables/useCurrentUser'

// The dashboard's connection card. Every user sees whether a connection exists and its name; only Admins (who own
// Connect) see the account and host, and get the Edit/Connect link. Everyone else is told whom to ask.
const { isAdmin } = useCurrentUser()
const status = ref<ConnectionStatus | null>(null)
const connection = ref<ErpConnectionInfo | null>(null)

onMounted(async () => {
  const [connStatus, conn] = await Promise.all([getConnectionStatus(), isAdmin.value ? getConnection() : null])
  status.value = connStatus
  connection.value = conn
})

// A relational source is named by its database and reached at host:port; ServiceNow by its instance URL.
const headline = computed(() =>
  status.value?.configured ? `Connected to ${status.value.name}` : 'No connection configured',
)
const target = computed(() =>
  connection.value?.host ? `${connection.value.host}:${connection.value.port}` : connection.value?.instanceUrl,
)
const askAdmin = computed(() => !status.value?.configured && !isAdmin.value)
</script>

<template>
  <Card>
    <div class="flex items-center gap-3">
      <span class="text-brand shrink-0"><Icon :icon="Plug" :size="24" /></span>
      <div class="min-w-0">
        <p class="m-0 text-sm font-semibold text-text-primary">{{ headline }}</p>
        <p v-if="askAdmin" class="m-0 text-xs text-text-secondary">Ask an Admin to set up the connection.</p>
        <p v-if="connection" class="m-0 text-xs text-text-secondary font-mono truncate">
          {{ connection.username }}@{{ target }}
        </p>
      </div>
      <RouterLink v-if="isAdmin" :to="{ name: 'connect' }" class="ml-auto text-brand text-sm shrink-0 hover:underline">
        {{ connection ? 'Edit' : 'Connect' }}
      </RouterLink>
    </div>
  </Card>
</template>
