<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { getConnection, saveConnection, invalidateConnectionCache } from '@/api/connection'
import { clearSession } from '@/api/auth'
import {
  DEFAULT_PORTS,
  emptyForm,
  formFromStored,
  isRelational as isRelationalType,
  portError as formPortError,
  storedConnectionLabel,
  toConnectionConfig,
  validateConnectionForm,
  withSourceType,
  type SourceType,
} from '@/lib/connectionForm'
import { Check, X, ChevronRight } from 'lucide-vue-next'
import Icon from '@/components/ui/Icon.vue'
import Button from '@/components/ui/Button.vue'
import Input from '@/components/ui/Input.vue'
import Select from '@/components/ui/Select.vue'
import Alert from '@/components/ui/Alert.vue'
import HelpTooltip from '@/components/ui/HelpTooltip.vue'
import PageHeader from '@/components/ui/PageHeader.vue'
import ConnectionTlsSelect from '@/components/ConnectionTlsSelect.vue'
import { useToasts } from '@/composables/useToasts'

const toasts = useToasts()

const router = useRouter()
const route = useRoute()

const form = ref(emptyForm())
// True when the server already holds a password — an empty field then means "keep it".
const hasStoredPassword = ref(false)
// Required-field errors are shown once the user has tried to submit (the port range check shows right away).
const submitted = ref(false)

const isRelational = computed(() => isRelationalType(form.value.sourceType))
const portError = computed(() => formPortError(form.value) ?? undefined)
const portPlaceholder = computed(() => (form.value.sourceType === 'mariadb' ? DEFAULT_PORTS.mariadb : DEFAULT_PORTS.postgres))
const fieldErrors = computed(() => validateConnectionForm(form.value))
const shownError = (key: string) => (submitted.value ? fieldErrors.value[key] : undefined)
const passwordPlaceholder = computed(() => (hasStoredPassword.value ? 'unchanged — leave empty to keep' : '••••••••'))
const failureHint = computed(() =>
  isRelational.value
    ? 'Connection failed. Check host, port, credentials, and that the database is reachable.'
    : 'Connection failed. Check the instance URL, credentials, and that the instance is reachable.',
)

function onSourceTypeChange(next: string) {
  form.value = withSourceType(form.value, next as SourceType)
  submitted.value = false
}

const testing = ref(false)
const testStatus = ref<'idle' | 'ok' | 'error'>('idle')
const testMessage = ref('')
const connectedLabel = ref<string | null>(null)

onMounted(async () => {
  const stored = await getConnection()
  if (!stored) return
  form.value = formFromStored(stored)
  hasStoredPassword.value = stored.hasPassword ?? false
  connectedLabel.value = storedConnectionLabel(stored)
})

function fail(message: string) {
  testStatus.value = 'error'
  testMessage.value = message
}

async function testConnection() {
  submitted.value = true
  const firstError = portError.value ?? Object.values(fieldErrors.value)[0]
  if (firstError) return fail(firstError)

  testing.value = true
  testStatus.value = 'idle'
  testMessage.value = ''
  try {
    const result = await saveConnection(toConnectionConfig(form.value))
    if ('schema' in result) {
      invalidateConnectionCache()
      connectedLabel.value = result.schema.connectionLabel
      hasStoredPassword.value ||= form.value.password !== ''
      form.value.password = ''
      testStatus.value = 'ok'
      testMessage.value = `Connected — found ${result.schema.tables.length} tables in "${result.schema.connectionLabel}".`
      toasts.success('Connection saved.')
    } else if (result.status === 401) {
      clearSession()
      router.push({ name: 'login' })
    } else {
      fail(result.error || failureHint.value)
      toasts.error(testMessage.value)
    }
  } catch {
    fail('Could not reach the backend. Is the backend service running?')
    toasts.error(testMessage.value)
  } finally {
    testing.value = false
  }
}

function proceed() {
  router.push({ name: 'source-schema' })
}
</script>

<template>
  <PageHeader title="Connect to Source System">
    <template #help>
      <HelpTooltip label="About the source connection" title="What am I connecting to?">
        <p>
          This is the system behind your ERP — a PostgreSQL or MariaDB/MySQL database, or a
          ServiceNow instance. The connector reads tables and rows from it, and (for import jobs,
          PostgreSQL only) writes confirmation data back into it.
        </p>
        <p>
          <strong>Example:</strong> <code>host=erp-db.internal port=5432 database=erp_prod</code>.
          Use a dedicated, read-mostly account rather than a superuser — the connector only ever
          needs the tables you explicitly map, and import jobs are further limited to their own
          allowed-columns list.
        </p>
      </HelpTooltip>
    </template>
  </PageHeader>

  <p class="text-text-secondary text-sm mt-2 mb-4 leading-relaxed">
    Choose the type of source system and enter its connection details.
    The connector will read the schema and data from it.
  </p>

  <Alert v-if="route.query.notice === 'needs-connection'" variant="warning" class="mb-4">
    A database connection is required before accessing that page.
    Configure and test your connection below, then proceed.
  </Alert>

  <Alert v-if="connectedLabel" variant="success" class="mb-6">
    <strong>Connected:</strong> {{ connectedLabel }}
  </Alert>
  <Alert v-else variant="info" class="mb-6">
    <strong>No connection configured yet.</strong>
    Enter the connection details for the source system below
    and click <em>Test Connection</em> to verify and save.
    <br />
    Running the docker-compose dev stack? Use host <code>testdb</code> — the API runs in its
    own container, so <code>localhost</code> is not reachable from there.
  </Alert>

  <form class="flex flex-col gap-4" novalidate @submit.prevent="testConnection">
    <Select id="source-type" :model-value="form.sourceType" label="Source Type" @update:model-value="onSourceTypeChange">
      <option value="postgres">PostgreSQL</option>
      <option value="mariadb">MariaDB / MySQL</option>
      <option value="servicenow">ServiceNow</option>
    </Select>

    <template v-if="isRelational">
      <div class="flex gap-3">
        <Input
          id="host"
          v-model="form.host"
          label="Host"
          placeholder="testdb (docker) / localhost"
          :error="shownError('host')"
          class="flex-1"
        />
        <Input
          id="port"
          v-model="form.port"
          label="Port"
          :placeholder="portPlaceholder"
          :error="portError"
          class="w-22.5 shrink-0"
        />
      </div>

      <Input id="database" v-model="form.database" label="Database" placeholder="my_erp_database" :error="shownError('database')" />
    </template>

    <template v-else>
      <Input
        id="instance-url"
        v-model="form.instanceUrl"
        label="Instance URL"
        placeholder="https://acme.service-now.com"
        :error="shownError('instanceUrl')"
      />
      <Select
        id="access-method"
        v-model="form.accessMethod"
        label="Access Method"
        help-text="Table API reads records over ServiceNow's REST Table API and respects the account's ACLs — no admin role needed."
      >
        <option value="table">Table API</option>
        <option value="sql">SQL API / Live Connect</option>
      </Select>
    </template>

    <div class="flex gap-3">
      <Input id="username" v-model="form.username" label="Username" placeholder="readonly_user" :error="shownError('username')" class="flex-1" />
      <Input
        id="password"
        v-model="form.password"
        type="password"
        label="Password"
        :placeholder="passwordPlaceholder"
        autocomplete="new-password"
        class="flex-1"
      />
    </div>

    <ConnectionTlsSelect v-if="form.sourceType !== 'servicenow'" v-model="form.sslMode" :source-type="form.sourceType" />

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
</template>
