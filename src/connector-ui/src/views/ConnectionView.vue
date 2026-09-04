<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { getConnection, saveConnection, invalidateConnectionCache } from '@/api/connection'
import { clearSession } from '@/api/auth'
import { Check, X, ChevronRight } from 'lucide-vue-next'
import Icon from '@/components/ui/Icon.vue'
import Button from '@/components/ui/Button.vue'
import Input from '@/components/ui/Input.vue'
import Alert from '@/components/ui/Alert.vue'

const router = useRouter()
const route = useRoute()

const host = ref('')
const port = ref('5432')
const database = ref('')
const username = ref('')
const password = ref('')

const portError = computed(() => {
  const n = Number(port.value)
  if (!Number.isInteger(n) || n < 1 || n > 65535) return 'Port must be a number between 1 and 65535.'
  return null
})

const testing = ref(false)
const testStatus = ref<'idle' | 'ok' | 'error'>('idle')
const testMessage = ref('')
const connectedLabel = ref<string | null>(null)

onMounted(async () => {
  const stored = await getConnection()
  if (stored) {
    host.value = stored.host
    port.value = String(stored.port)
    database.value = stored.database
    username.value = stored.username
    connectedLabel.value = `${stored.host}:${stored.port}/${stored.database}`
  }
})

async function testConnection() {
  if (portError.value) {
    testStatus.value = 'error'
    testMessage.value = portError.value
    return
  }
  testing.value = true
  testStatus.value = 'idle'
  testMessage.value = ''
  try {
    const result = await saveConnection({
      host: host.value,
      port: Number(port.value),
      database: database.value,
      username: username.value,
      password: password.value,
    })
    if ('schema' in result) {
      invalidateConnectionCache()
      connectedLabel.value = result.schema.connectionLabel
      testStatus.value = 'ok'
      testMessage.value = `Connected — found ${result.schema.tables.length} tables in "${result.schema.connectionLabel}".`
    } else {
      if (result.status === 401) {
        clearSession()
        router.push({ name: 'login' })
        return
      }
      testStatus.value = 'error'
      testMessage.value = result.error || 'Connection failed. Check host, port, credentials, and that the database is reachable.'
    }
  } catch {
    testStatus.value = 'error'
    testMessage.value = 'Could not reach the backend. Is the backend service running?'
  } finally {
    testing.value = false
  }
}

function proceed() {
  router.push({ name: 'source-schema' })
}
</script>

<template>
  <div class="max-w-xl">
    <div class="flex items-center gap-3 mb-2">
      <span class="bg-brand text-white px-2.5 py-0.5 rounded-full text-xs font-bold tracking-wide shrink-0">Step 1</span>
      <h1 class="m-0 text-xl font-semibold text-text-primary">Connect to Source Database</h1>
    </div>

    <p class="text-text-secondary text-sm mt-2 mb-4 leading-relaxed">
      Enter the connection details for the PostgreSQL database you want to read data from.
      The connector will read the schema and data from this database.
    </p>

    <Alert v-if="route.query.notice === 'needs-connection'" variant="warning" class="mb-4">
      A database connection is required before accessing that step.
      Configure and test your connection below, then proceed.
    </Alert>

    <Alert v-if="connectedLabel" variant="success" class="mb-6">
      <strong>Connected:</strong> {{ connectedLabel }}
    </Alert>
    <Alert v-else variant="info" class="mb-6">
      <strong>No connection configured yet.</strong>
      Enter the PostgreSQL connection details for the source ERP database below
      and click <em>Test Connection</em> to verify and save.
      <br />
      Running the docker-compose dev stack? Use host <code>testdb</code> — the API runs in its
      own container, so <code>localhost</code> only works when running via <code>./dev.sh</code>.
    </Alert>

    <form class="flex flex-col gap-4" @submit.prevent="testConnection">
      <div class="flex gap-3">
        <Input id="host" v-model="host" label="Host" placeholder="testdb (docker) / localhost" class="flex-1" />
        <Input id="port" v-model="port" label="Port" placeholder="5432" :error="portError ?? undefined" class="w-22.5 shrink-0" />
      </div>

      <Input id="database" v-model="database" label="Database" placeholder="my_erp_database" />

      <div class="flex gap-3">
        <Input id="username" v-model="username" label="Username" placeholder="readonly_user" class="flex-1" />
        <Input id="password" v-model="password" type="password" label="Password" placeholder="••••••••" class="flex-1" />
      </div>

      <div class="flex gap-3 mt-1">
        <Button type="submit" variant="secondary" :loading="testing">
          {{ testing ? 'Testing…' : 'Test Connection' }}
        </Button>
        <Button type="button" variant="primary" @click="proceed">
          Proceed to Source Schema
          <Icon :icon="ChevronRight" :size="16" />
        </Button>
      </div>
    </form>

    <Alert v-if="testStatus === 'ok'" variant="success" class="mt-4">
      <template #icon><Icon :icon="Check" :size="16" /></template>
      {{ testMessage }}
    </Alert>
    <Alert v-else-if="testStatus === 'error'" variant="danger" class="mt-4">
      <template #icon><Icon :icon="X" :size="16" /></template>
      {{ testMessage }}
    </Alert>
  </div>
</template>
