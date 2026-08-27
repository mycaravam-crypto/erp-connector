<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { getExportDefinition, type ExportDefinition } from '@/api/exportDefinitions'
import ExportDefinitionBasicFields from '@/components/ExportDefinitionBasicFields.vue'
import ExportDefinitionRunControls from '@/components/ExportDefinitionRunControls.vue'
import ExportNodeFieldEditor from '@/components/ExportNodeFieldEditor.vue'

const route = useRoute()
const id = computed(() => Number(route.params.id))

const definition = ref<ExportDefinition | null>(null)
const loading = ref(true)
const loadError = ref<string | null>(null)
const notFound = ref(false)

async function load() {
  loading.value = true
  loadError.value = null
  notFound.value = false
  try {
    const result = await getExportDefinition(id.value)
    if (result === null) {
      notFound.value = true
    } else {
      definition.value = result
    }
  } catch {
    loadError.value = 'Could not reach the API. Is the backend service running?'
  } finally {
    loading.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="max-w-3xl">
    <RouterLink
      :to="{ name: 'export-definitions' }"
      class="inline-block text-indigo-600 text-sm no-underline hover:underline mb-4"
    >← Back to list</RouterLink>

    <p v-if="loading" class="text-slate-500">Loading…</p>
    <p v-else-if="notFound" class="text-red-600">Export definition not found.</p>
    <p v-else-if="loadError" class="text-red-600">{{ loadError }}</p>

    <template v-else-if="definition">
      <h1 class="m-0 text-xl font-semibold mb-1">{{ definition.name }}</h1>
      <p class="text-slate-500 text-sm mt-1 mb-5">
        Config version {{ definition.configVersion }} · created by {{ definition.createdBy }}
      </p>

      <ExportDefinitionBasicFields :definition="definition" />

      <h2 class="text-base font-semibold text-slate-900 mb-2.5">Fields</h2>
      <p class="text-slate-500 text-sm mb-3 leading-relaxed">
        Correct the source column / related table / join key names below so they match your actual
        source database, then save. This edits identifiers on the existing tree only — it does not
        add, remove, or restructure fields.
      </p>
      <ExportNodeFieldEditor :nodes="definition.rootNode.children" :depth="0" class="mb-6" />

      <ExportDefinitionRunControls :definition="definition" />
    </template>
  </div>
</template>
