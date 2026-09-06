<script setup lang="ts">
import { ref } from 'vue'
import { saveSchedulerConfig, type SchedulerConfig } from '@/api/scheduler'
import { Check, X } from 'lucide-vue-next'
import Icon from '@/components/ui/Icon.vue'
import Input from '@/components/ui/Input.vue'
import Select from '@/components/ui/Select.vue'
import Button from '@/components/ui/Button.vue'
import Alert from '@/components/ui/Alert.vue'

const props = defineProps<{ config: SchedulerConfig }>()

const scheduledTime = ref(props.config.scheduledTimeUtc)
const retentionDays = ref(props.config.retentionDays)
const format = ref(props.config.format)

const saving = ref(false)
const saveStatus = ref<'idle' | 'ok' | 'error'>('idle')
const saveMessage = ref('')

async function save() {
  saving.value = true
  saveStatus.value = 'idle'
  saveMessage.value = ''
  try {
    const result = await saveSchedulerConfig({
      scheduledTimeUtc: scheduledTime.value,
      retentionDays: retentionDays.value,
      format: format.value,
    })
    if (result.ok) {
      saveStatus.value = 'ok'
      saveMessage.value = 'Settings saved. The new schedule takes effect on the next export cycle.'
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
  <section class="mt-6">
    <h2 class="text-base font-semibold text-text-primary mb-1">Export Scheduler</h2>
    <p class="text-text-secondary text-sm mb-4 leading-relaxed">
      The scheduled export runs once daily at the configured UTC time.
      Changes take effect on the next export cycle — no restart required.
    </p>

    <form class="flex flex-col gap-4" @submit.prevent="save">
      <div class="flex gap-6">
        <Input id="scheduled-time" v-model="scheduledTime" type="time" label="Daily run time (UTC)" class="w-36" />

        <Input
          id="retention-days"
          v-model.number="retentionDays"
          type="number"
          :min="1"
          :max="3650"
          label="Retention (days)"
          help-text="1–3,650 days. Export runs and staging files older than this are deleted."
          class="w-28"
        />

        <Select id="scheduled-format" v-model="format" label="Export format" help-text="Nested JSON groups in the mapping only apply when set to JSON." class="w-28">
          <option value="xlsx">Excel</option>
          <option value="csv">CSV</option>
          <option value="json">JSON</option>
        </Select>
      </div>

      <div>
        <Button type="submit" variant="secondary" :loading="saving">
          {{ saving ? 'Saving…' : 'Save Settings' }}
        </Button>
      </div>
    </form>

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
