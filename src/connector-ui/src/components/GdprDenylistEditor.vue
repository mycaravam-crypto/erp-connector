<script setup lang="ts">
import { ref } from 'vue'
import { saveGdprDeniedFields } from '@/api/scheduler'
import { Check, X } from 'lucide-vue-next'
import Icon from '@/components/ui/Icon.vue'
import Input from '@/components/ui/Input.vue'
import Button from '@/components/ui/Button.vue'
import Alert from '@/components/ui/Alert.vue'

const props = defineProps<{ initialFields: string[] }>()

const deniedFields = ref<string[]>([...props.initialFields])
const newField = ref('')
const saving = ref(false)
const saveStatus = ref<'idle' | 'ok' | 'error'>('idle')
const saveMessage = ref('')

function addField() {
  const f = newField.value.trim()
  if (f && !deniedFields.value.includes(f)) {
    deniedFields.value = [...deniedFields.value, f]
  }
  newField.value = ''
}

function removeField(field: string) {
  deniedFields.value = deniedFields.value.filter((f) => f !== field)
}

async function save() {
  saving.value = true
  saveStatus.value = 'idle'
  saveMessage.value = ''
  try {
    const result = await saveGdprDeniedFields(deniedFields.value)
    if (result.ok) {
      saveStatus.value = 'ok'
      saveMessage.value = 'GDPR denylist saved. Changes take effect immediately.'
    } else {
      saveStatus.value = 'error'
      saveMessage.value = result.error ?? 'Unknown error.'
    }
  } catch {
    saveStatus.value = 'error'
    saveMessage.value = 'Could not reach the backend. Is the backend service running?'
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <section class="mt-10">
    <h2 class="text-base font-semibold text-text-primary mb-1">GDPR Denied Fields</h2>
    <p class="text-text-secondary text-sm mb-4 leading-relaxed">
      These fields are stripped from all exports at query time (GDPR Art. 5(1)(c)).
      Changes take effect immediately.
    </p>

    <div class="flex flex-wrap gap-2 mb-4">
      <span
        v-for="field in deniedFields"
        :key="field"
        class="inline-flex items-center gap-1 px-2.5 py-1 rounded-full bg-surface-elevated border border-border-strong text-text-primary text-xs font-medium"
      >
        {{ field }}
        <button
          type="button"
          class="ml-0.5 text-text-secondary hover:text-text-primary cursor-pointer leading-none bg-transparent border-none p-0"
          :aria-label="`Remove ${field}`"
          @click="removeField(field)"
        >
          <Icon :icon="X" :size="16" />
        </button>
      </span>
      <span v-if="deniedFields.length === 0" class="text-xs text-text-muted italic">No fields configured.</span>
    </div>

    <div class="flex gap-2 mb-4">
      <Input v-model="newField" placeholder="Field name to add…" class="w-56" @keydown.enter.prevent="addField" />
      <Button variant="secondary" @click="addField">Add</Button>
    </div>

    <Button variant="secondary" :loading="saving" @click="save">
      {{ saving ? 'Saving…' : 'Save GDPR Denylist' }}
    </Button>

    <Alert v-if="saveStatus === 'ok'" variant="success" class="mt-4">
      <template #icon><Icon :icon="Check" :size="16" /></template>
      {{ saveMessage }}
    </Alert>
    <Alert v-else-if="saveStatus === 'error'" variant="danger" class="mt-4">
      <template #icon><Icon :icon="X" :size="16" /></template>
      {{ saveMessage }}
    </Alert>
  </section>
</template>
