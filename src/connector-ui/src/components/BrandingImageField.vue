<script setup lang="ts">
import { useId, ref } from 'vue'
import { X } from 'lucide-vue-next'
import Icon from '@/components/ui/Icon.vue'
import Button from '@/components/ui/Button.vue'
import FieldShell from '@/components/ui/FieldShell.vue'

const props = defineProps<{
  label: string
  helpText?: string
  accept: string
  maxBytes: number
  /** Preview thumbnail shape — logo/favicon read best as a small square, a background as a wide strip. */
  previewShape?: 'square' | 'wide'
}>()

const model = defineModel<string | null>({ default: null })

const id = useId()
const helpId = useId()
const errorId = useId()
const error = ref<string | null>(null)
const fileInput = ref<HTMLInputElement>()

function maxBytesLabel(): string {
  const kb = props.maxBytes / 1024
  return kb >= 1024 ? `${(kb / 1024).toFixed(kb % 1024 === 0 ? 0 : 1)}MB` : `${kb}KB`
}

function onFileChange(event: Event) {
  error.value = null
  const file = (event.target as HTMLInputElement).files?.[0]
  if (!file) return

  if (!file.type.startsWith('image/')) {
    error.value = 'Please choose an image file.'
    return
  }
  if (file.size > props.maxBytes) {
    error.value = `Image is too large — must be under ${maxBytesLabel()}.`
    return
  }

  const reader = new FileReader()
  reader.onload = () => {
    model.value = reader.result as string
  }
  reader.onerror = () => {
    error.value = 'Could not read that file.'
  }
  reader.readAsDataURL(file)
}

function remove() {
  model.value = null
  error.value = null
  if (fileInput.value) fileInput.value.value = ''
}
</script>

<template>
  <FieldShell :id="id" :help-id="helpId" :error-id="errorId" :label="label" :help-text="helpText" :error="error ?? undefined">
    <div class="flex items-center gap-3">
      <div
        class="flex items-center justify-center shrink-0 rounded-md border border-border-strong bg-surface-elevated overflow-hidden"
        :class="previewShape === 'wide' ? 'w-24 h-12' : 'w-12 h-12'"
      >
        <img v-if="model" :src="model" alt="" class="w-full h-full object-cover" />
        <span v-else class="text-xs text-text-muted">None</span>
      </div>

      <input :id="id" ref="fileInput" type="file" :accept="accept" class="hidden" @change="onFileChange" />
      <Button type="button" variant="secondary" @click="fileInput?.click()">Choose file…</Button>
      <Button v-if="model" type="button" variant="ghost" @click="remove">
        <template #icon><Icon :icon="X" :size="16" /></template>
        Remove
      </Button>
    </div>
  </FieldShell>
</template>
