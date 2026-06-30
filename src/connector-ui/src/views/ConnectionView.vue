<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { getConnection, saveConnection } from '@/api/connection'

const router = useRouter()

const host = ref('localhost')
const port = ref('5432')
const database = ref('')
const username = ref('')
const password = ref('')

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
  testing.value = true
  testStatus.value = 'idle'
  testMessage.value = ''
  try {
    const schema = await saveConnection({
      host: host.value,
      port: Number(port.value) || 5432,
      database: database.value,
      username: username.value,
      password: password.value,
    })
    if (schema) {
      connectedLabel.value = schema.connectionLabel
      testStatus.value = 'ok'
      testMessage.value = `Connected — found ${schema.tables.length} tables in "${schema.connectionLabel}".`
    } else {
      testStatus.value = 'error'
      testMessage.value = 'Connection failed. Check host, port, credentials, and that the database is reachable.'
    }
  } catch {
    testStatus.value = 'error'
    testMessage.value = 'Could not reach the backend. Is the server running on :5189?'
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
      <span class="bg-slate-900 text-slate-200 px-2.5 py-0.5 rounded-full text-xs font-bold tracking-wide shrink-0">Step 1</span>
      <h1 class="m-0 text-xl font-semibold">Connect to Source Database</h1>
    </div>

    <p class="text-slate-500 text-sm mt-2 mb-4 leading-relaxed">
      Enter the connection details for the PostgreSQL database you want to read data from.
      The connector will read the schema and data from this database.
    </p>

    <div v-if="connectedLabel" class="bg-green-50 border border-green-200 rounded-lg px-4 py-3 text-sm text-green-800 mb-6">
      <strong>Connected:</strong> {{ connectedLabel }}
    </div>
    <div v-else class="bg-yellow-50 border border-yellow-300 rounded-lg px-4 py-3 text-sm text-yellow-900 mb-6 leading-relaxed">
      <strong>Demo mode active.</strong>
      The current backend uses a built-in SQLite demo database that mirrors a real PostgreSQL schema.
      Fill in the fields below to configure the production connection; click
      <em>Test Connection</em> to verify and save.
    </div>

    <form class="flex flex-col gap-4" @submit.prevent="testConnection">
      <div class="flex gap-3">
        <div class="flex flex-col gap-1 flex-1">
          <label for="host" class="text-xs font-semibold text-slate-700">Host</label>
          <input id="host" v-model="host" type="text" placeholder="localhost"
            class="px-2.5 py-2 border border-slate-300 rounded-md text-sm text-slate-900 bg-white outline-none focus:outline-indigo-600 focus:outline-2 focus:border-transparent" />
        </div>
        <div class="flex flex-col gap-1 w-22.5 shrink-0">
          <label for="port" class="text-xs font-semibold text-slate-700">Port</label>
          <input id="port" v-model="port" type="text" placeholder="5432"
            class="px-2.5 py-2 border border-slate-300 rounded-md text-sm text-slate-900 bg-white outline-none focus:outline-indigo-600 focus:outline-2 focus:border-transparent" />
        </div>
      </div>

      <div class="flex flex-col gap-1">
        <label for="database" class="text-xs font-semibold text-slate-700">Database</label>
        <input id="database" v-model="database" type="text" placeholder="my_erp_database"
          class="px-2.5 py-2 border border-slate-300 rounded-md text-sm text-slate-900 bg-white outline-none focus:outline-indigo-600 focus:outline-2 focus:border-transparent" />
      </div>

      <div class="flex gap-3">
        <div class="flex flex-col gap-1 flex-1">
          <label for="username" class="text-xs font-semibold text-slate-700">Username</label>
          <input id="username" v-model="username" type="text" placeholder="readonly_user"
            class="px-2.5 py-2 border border-slate-300 rounded-md text-sm text-slate-900 bg-white outline-none focus:outline-indigo-600 focus:outline-2 focus:border-transparent" />
        </div>
        <div class="flex flex-col gap-1 flex-1">
          <label for="password" class="text-xs font-semibold text-slate-700">Password</label>
          <input id="password" v-model="password" type="password" placeholder="••••••••"
            class="px-2.5 py-2 border border-slate-300 rounded-md text-sm text-slate-900 bg-white outline-none focus:outline-indigo-600 focus:outline-2 focus:border-transparent" />
        </div>
      </div>

      <div class="flex gap-3 mt-1">
        <button type="submit"
          class="px-5 py-2 border border-slate-400 rounded-md bg-white text-slate-900 text-sm font-semibold cursor-pointer hover:enabled:bg-slate-50 disabled:opacity-50 disabled:cursor-not-allowed"
          :disabled="testing">
          {{ testing ? 'Testing…' : 'Test Connection' }}
        </button>
        <button type="button"
          class="px-5 py-2 border-0 rounded-md bg-slate-900 text-slate-200 text-sm font-semibold cursor-pointer hover:bg-slate-800"
          @click="proceed">
          Proceed to Source Schema →
        </button>
      </div>
    </form>

    <div v-if="testStatus === 'ok'" class="flex items-center gap-2 mt-4 px-4 py-3 rounded-md bg-green-50 border border-green-200 text-green-800 text-sm">
      <span class="font-bold">✓</span>
      {{ testMessage }}
    </div>
    <div v-else-if="testStatus === 'error'" class="flex items-center gap-2 mt-4 px-4 py-3 rounded-md bg-red-50 border border-red-200 text-red-800 text-sm">
      <span class="font-bold">✕</span>
      {{ testMessage }}
    </div>
  </div>
</template>
