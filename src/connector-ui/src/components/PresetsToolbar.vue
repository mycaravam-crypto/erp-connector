<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { getPresets, savePreset, deletePreset, type ExportMappingConfig } from '@/api/erp'

const props = defineProps<{
  canSave: boolean
  getConfig: () => ExportMappingConfig
}>()

const emit = defineEmits<{
  apply: [config: ExportMappingConfig]
}>()

const presets = ref<Record<string, ExportMappingConfig>>({})
const presetNames = computed(() => Object.keys(presets.value).sort())
const selected = ref('')
const saving = ref(false)
const error = ref<string | null>(null)

const showSaveInput = ref(false)
const saveInputName = ref('')
const confirmingDelete = ref(false)

async function loadPresets() {
  try {
    presets.value = await getPresets()
  } catch {
    error.value = 'Could not load presets. Is the backend service running?'
  }
}

onMounted(loadPresets)

function applySelected() {
  const cfg = presets.value[selected.value]
  if (!cfg) return
  emit('apply', cfg)
  error.value = null
}

function openSaveInput() {
  if (!props.canSave) {
    error.value = 'Select a source table before saving a preset.'
    return
  }
  saveInputName.value = selected.value || ''
  showSaveInput.value = true
  error.value = null
}

async function confirmSaveAs() {
  const name = saveInputName.value.trim()
  if (!name) {
    error.value = 'Preset name cannot be empty.'
    return
  }

  saving.value = true
  error.value = null
  const result = await savePreset(name, props.getConfig())
  saving.value = false

  if (!result.ok) {
    error.value = result.error ?? 'Failed to save preset.'
    return
  }
  await loadPresets()
  selected.value = name
  showSaveInput.value = false
}

async function confirmDelete() {
  if (!selected.value) return

  saving.value = true
  error.value = null
  const result = await deletePreset(selected.value)
  saving.value = false

  if (!result.ok) {
    error.value = result.error ?? 'Failed to delete preset.'
    return
  }
  selected.value = ''
  confirmingDelete.value = false
  await loadPresets()
}
</script>

<template>
  <div class="mb-5">
    <div class="flex items-center gap-2 flex-wrap">
      <h2 class="text-base font-semibold text-slate-900 shrink-0">Presets</h2>
      <select
        class="preset-select flex-1 min-w-48 px-2.5 py-2 border border-slate-300 rounded-md text-sm text-slate-900 bg-white cursor-pointer"
        v-model="selected"
      >
        <option value="" disabled>— select a preset —</option>
        <option v-for="name in presetNames" :key="name" :value="name">{{ name }}</option>
      </select>
      <button
        class="load-btn px-3 py-2 border border-slate-300 rounded-md bg-white text-sm text-slate-700 cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed hover:enabled:bg-slate-50"
        :disabled="!selected || saving"
        @click="applySelected"
      >Load</button>
      <button
        class="save-as-btn px-3 py-2 border border-slate-300 rounded-md bg-white text-sm text-slate-700 cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed hover:enabled:bg-slate-50"
        :disabled="saving"
        @click="openSaveInput"
      >Save As…</button>
      <button
        class="delete-preset-btn px-3 py-2 border border-red-200 rounded-md bg-white text-sm text-red-600 cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed hover:enabled:bg-red-50"
        :disabled="!selected || saving"
        @click="confirmingDelete = true"
      >Delete</button>
    </div>

    <!-- Inline save-as input -->
    <div v-if="showSaveInput" class="flex items-center gap-2 mt-2 flex-wrap">
      <input
        class="px-2.5 py-1.5 border border-slate-300 rounded-md text-sm text-slate-900 bg-white outline-none focus:border-slate-900 min-w-48 flex-1"
        type="text"
        placeholder="Preset name…"
        v-model="saveInputName"
        @keyup.enter="confirmSaveAs"
        @keyup.esc="showSaveInput = false"
      />
      <button
        class="px-3 py-1.5 border-0 rounded-md bg-slate-900 text-slate-200 text-sm font-semibold cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed hover:enabled:bg-slate-800"
        :disabled="saving || !saveInputName.trim()"
        @click="confirmSaveAs"
      >{{ saving ? 'Saving…' : 'Save' }}</button>
      <button
        class="px-3 py-1.5 border border-slate-300 rounded-md bg-white text-sm text-slate-500 cursor-pointer hover:bg-slate-50"
        @click="showSaveInput = false"
      >Cancel</button>
    </div>

    <!-- Inline delete confirm -->
    <div v-if="confirmingDelete" class="flex items-center gap-2 mt-2 text-sm">
      <span class="text-red-700 font-semibold">Delete "{{ selected }}"?</span>
      <button
        class="px-3 py-1 border border-red-300 rounded-md bg-red-50 text-red-700 text-sm font-semibold cursor-pointer disabled:opacity-50 hover:bg-red-100"
        :disabled="saving"
        @click="confirmDelete"
      >{{ saving ? 'Deleting…' : 'Yes, delete' }}</button>
      <button
        class="px-3 py-1 border border-slate-300 rounded-md bg-white text-sm text-slate-500 cursor-pointer hover:bg-slate-50"
        @click="confirmingDelete = false"
      >Cancel</button>
    </div>
  </div>
  <div v-if="error" class="preset-error px-3.5 py-2.5 bg-red-50 border border-red-200 rounded-md text-sm text-red-600 mb-5">{{ error }}</div>
</template>
