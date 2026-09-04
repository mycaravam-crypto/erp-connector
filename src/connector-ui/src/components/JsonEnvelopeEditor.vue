<script setup lang="ts">
import type { ExportJsonWrapperConfig } from '@/api/mapping'
import { X } from 'lucide-vue-next'
import Icon from '@/components/ui/Icon.vue'

const wrapper = defineModel<ExportJsonWrapperConfig | null>({ required: true })
const emit = defineEmits<{ dirty: [] }>()

function enable() {
  wrapper.value = { rootKey: '', itemsKey: 'records', metadataKey: 'metadata', metadataFields: [] }
  emit('dirty')
}

function disable() {
  wrapper.value = null
  emit('dirty')
}

function addMetadataField() {
  wrapper.value?.metadataFields.push({ key: '', value: '', isDynamicTimestamp: false })
  emit('dirty')
}

function removeMetadataField(idx: number) {
  wrapper.value?.metadataFields.splice(idx, 1)
  emit('dirty')
}
</script>

<template>
  <div class="mb-7">
    <h2 class="text-base font-semibold text-slate-900 mb-1">JSON Envelope</h2>
    <p class="text-sm text-slate-500 mb-3 leading-snug">
      Applies to JSON export only. By default the export uses <code>{ schema_version, extracted_at, records }</code>.
      Customize it to match a target system's expected shape.
    </p>

    <div v-if="!wrapper" class="flex items-center gap-3">
      <span class="text-sm text-slate-500">Using the default envelope.</span>
      <button class="customize-wrapper-btn px-3 py-1.5 border border-slate-300 rounded-md bg-white text-sm text-slate-700 cursor-pointer hover:bg-slate-50" @click="enable">Customize…</button>
    </div>

    <div v-else class="border border-slate-200 rounded-lg px-4 py-3 bg-white flex flex-col gap-3">
      <div class="flex justify-end">
        <button class="reset-wrapper-btn px-2.5 py-1 border border-slate-300 rounded text-xs text-slate-500 bg-white cursor-pointer hover:bg-slate-50" @click="disable">Reset to default</button>
      </div>
      <div class="flex gap-2.5 flex-wrap">
        <div class="flex flex-col gap-1 flex-1 min-w-36">
          <label class="text-[0.7rem] font-semibold text-slate-500 uppercase tracking-wide">Root Key</label>
          <input class="root-key-input px-2 py-1.5 border border-slate-300 rounded text-sm text-slate-900 bg-white w-full outline-none focus:border-slate-900" type="text" v-model="wrapper.rootKey" placeholder="e.g. masterData — blank for none" @input="emit('dirty')" />
        </div>
        <div class="flex flex-col gap-1 flex-1 min-w-36">
          <label class="text-[0.7rem] font-semibold text-slate-500 uppercase tracking-wide">Items Key</label>
          <input class="items-key-input px-2 py-1.5 border border-slate-300 rounded text-sm text-slate-900 bg-white w-full outline-none focus:border-slate-900" type="text" v-model="wrapper.itemsKey" placeholder="records" @input="emit('dirty')" />
        </div>
        <div class="flex flex-col gap-1 flex-1 min-w-36">
          <label class="text-[0.7rem] font-semibold text-slate-500 uppercase tracking-wide">Metadata Key</label>
          <input class="metadata-key-input px-2 py-1.5 border border-slate-300 rounded text-sm text-slate-900 bg-white w-full outline-none focus:border-slate-900" type="text" v-model="wrapper.metadataKey" placeholder="metadata — blank flattens" @input="emit('dirty')" />
        </div>
      </div>

      <div>
        <div class="flex items-center gap-2 mb-1.5">
          <span class="text-[0.7rem] font-semibold text-slate-500 uppercase tracking-wide">Metadata Fields</span>
          <button type="button" class="add-metadata-field-btn ml-auto px-2 py-0.5 border border-slate-300 rounded text-[0.7rem] text-slate-600 bg-white cursor-pointer hover:bg-slate-100" @click="addMetadataField">+ Add Field</button>
        </div>
        <p v-if="wrapper.metadataFields.length === 0" class="text-[0.7rem] text-slate-400">None — falls back to schema_version/extracted_at.</p>
        <div v-for="(m, mIdx) in wrapper.metadataFields" :key="mIdx" class="flex items-center gap-2 mb-1.5">
          <input class="metadata-field-key-input flex-1 px-2 py-1 border border-slate-300 rounded text-xs font-mono text-slate-900 bg-white outline-none focus:border-slate-900" type="text" v-model="m.key" placeholder="key, e.g. version" @input="emit('dirty')" />
          <input class="metadata-field-value-input flex-1 px-2 py-1 border border-slate-300 rounded text-xs font-mono text-slate-900 bg-white outline-none focus:border-slate-900 disabled:bg-slate-50" type="text" v-model="m.value" :disabled="m.isDynamicTimestamp" placeholder="value, e.g. 1.0" @input="emit('dirty')" />
          <label class="flex items-center gap-1 text-[0.7rem] text-slate-500 whitespace-nowrap">
            <input type="checkbox" v-model="m.isDynamicTimestamp" @change="emit('dirty')" />
            Use export timestamp
          </label>
          <button type="button" class="remove-metadata-field-btn shrink-0 p-1 border border-red-200 rounded text-red-600 bg-white leading-none cursor-pointer hover:bg-red-50" @click="removeMetadataField(mIdx)" title="Remove field"><Icon :icon="X" :size="16" /></button>
        </div>
      </div>
    </div>
  </div>
</template>
