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
  <div class="page">
    <div class="step-header">
      <span class="step-badge">Step 1</span>
      <h1>Connect to Source Database</h1>
    </div>

    <p class="intro">
      Enter the connection details for the PostgreSQL database you want to read data from.
      The connector will read the schema and data from this database.
    </p>

    <div v-if="connectedLabel" class="connected-note">
      <strong>Connected:</strong> {{ connectedLabel }}
    </div>
    <div v-else class="demo-note">
      <strong>Demo mode active.</strong>
      The current backend uses a built-in SQLite demo database that mirrors a real PostgreSQL schema.
      Fill in the fields below to configure the production connection; click
      <em>Test Connection</em> to verify and save.
    </div>

    <form class="conn-form" @submit.prevent="testConnection">
      <div class="form-row">
        <div class="field field-grow">
          <label for="host">Host</label>
          <input id="host" v-model="host" type="text" placeholder="localhost" />
        </div>
        <div class="field field-port">
          <label for="port">Port</label>
          <input id="port" v-model="port" type="text" placeholder="5432" />
        </div>
      </div>

      <div class="field">
        <label for="database">Database</label>
        <input id="database" v-model="database" type="text" placeholder="my_erp_database" />
      </div>

      <div class="form-row">
        <div class="field field-grow">
          <label for="username">Username</label>
          <input id="username" v-model="username" type="text" placeholder="readonly_user" />
        </div>
        <div class="field field-grow">
          <label for="password">Password</label>
          <input id="password" v-model="password" type="password" placeholder="••••••••" />
        </div>
      </div>

      <div class="action-row">
        <button type="submit" class="btn-test" :disabled="testing">
          {{ testing ? 'Testing…' : 'Test Connection' }}
        </button>
        <button type="button" class="btn-next" @click="proceed">
          Proceed to Source Schema →
        </button>
      </div>
    </form>

    <div v-if="testStatus === 'ok'" class="status-banner status-ok">
      <span class="status-icon">✓</span>
      {{ testMessage }}
    </div>
    <div v-else-if="testStatus === 'error'" class="status-banner status-err">
      <span class="status-icon">✕</span>
      {{ testMessage }}
    </div>
  </div>
</template>

<style scoped>
.page {
  max-width: 640px;
}

.step-header {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-bottom: 0.5rem;
}

.step-badge {
  background: #1a1a2e;
  color: #e2e8f0;
  padding: 0.2rem 0.6rem;
  border-radius: 9999px;
  font-size: 0.75rem;
  font-weight: 700;
  letter-spacing: 0.04em;
  flex-shrink: 0;
}

h1 {
  margin: 0;
  font-size: 1.25rem;
}

.intro {
  color: #475569;
  font-size: 0.9rem;
  margin: 0.5rem 0 1rem;
  line-height: 1.6;
}

.demo-note {
  background: #fefce8;
  border: 1px solid #fde047;
  border-radius: 0.5rem;
  padding: 0.75rem 1rem;
  font-size: 0.85rem;
  color: #713f12;
  margin-bottom: 1.5rem;
  line-height: 1.5;
}

.connected-note {
  background: #f0fdf4;
  border: 1px solid #bbf7d0;
  border-radius: 0.5rem;
  padding: 0.75rem 1rem;
  font-size: 0.85rem;
  color: #166534;
  margin-bottom: 1.5rem;
  line-height: 1.5;
}

.conn-form {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.form-row {
  display: flex;
  gap: 0.75rem;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 0.3rem;
}

.field-grow { flex: 1; }
.field-port { width: 90px; flex-shrink: 0; }

label {
  font-size: 0.8rem;
  font-weight: 600;
  color: #374151;
}

input {
  padding: 0.45rem 0.65rem;
  border: 1px solid #cbd5e1;
  border-radius: 0.375rem;
  font-size: 0.9rem;
  color: #1e293b;
  background: #fff;
}

input:focus {
  outline: 2px solid #4f46e5;
  outline-offset: 1px;
  border-color: transparent;
}

.action-row {
  display: flex;
  gap: 0.75rem;
  margin-top: 0.25rem;
}

.btn-test {
  padding: 0.5rem 1.25rem;
  border: 1px solid #334155;
  border-radius: 0.375rem;
  background: #fff;
  color: #1e293b;
  font-size: 0.9rem;
  font-weight: 600;
  cursor: pointer;
}

.btn-test:hover:not(:disabled) { background: #f1f5f9; }
.btn-test:disabled { opacity: 0.5; cursor: not-allowed; }

.btn-next {
  padding: 0.5rem 1.25rem;
  border: none;
  border-radius: 0.375rem;
  background: #1a1a2e;
  color: #e2e8f0;
  font-size: 0.9rem;
  font-weight: 600;
  cursor: pointer;
}

.btn-next:hover { background: #2d2d4e; }

.status-banner {
  display: flex;
  align-items: center;
  gap: 0.6rem;
  margin-top: 1rem;
  padding: 0.75rem 1rem;
  border-radius: 0.375rem;
  font-size: 0.875rem;
}

.status-ok  { background: #f0fdf4; border: 1px solid #bbf7d0; color: #166534; }
.status-err { background: #fef2f2; border: 1px solid #fecaca; color: #991b1b; }

.status-icon { font-weight: 700; }
</style>
