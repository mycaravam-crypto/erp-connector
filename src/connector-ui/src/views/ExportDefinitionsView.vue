<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { RouterLink } from 'vue-router'
import { listExportDefinitions, type ExportDefinitionSummary } from '@/api/exportDefinitions'

const definitions = ref<ExportDefinitionSummary[]>([])
const loading = ref(true)
const loadError = ref<string | null>(null)

async function load() {
  loading.value = true
  loadError.value = null
  try {
    definitions.value = await listExportDefinitions()
  } catch {
    loadError.value = 'Could not load export definitions. Is the backend service running?'
  } finally {
    loading.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="max-w-4xl">
    <div class="flex items-center justify-between gap-3 mb-2">
      <h1 class="m-0 text-xl font-semibold">Export Definitions</h1>
      <button
        type="button"
        class="px-4 py-1.5 border border-slate-400 rounded-md bg-white text-slate-900 text-sm font-semibold cursor-pointer hover:bg-slate-50 disabled:opacity-50"
        :disabled="loading"
        @click="load"
      >
        {{ loading ? 'Loading…' : 'Refresh' }}
      </button>
    </div>

    <p class="text-slate-500 text-sm mt-2 mb-5 leading-relaxed">
      Saved, independently triggerable export configs. Definitions here were migrated automatically
      from the legacy mapping screen the first time it had a config to convert — edit one to point it
      at the right tables/columns for your actual source database.
    </p>

    <div v-if="loading && definitions.length === 0" class="text-slate-500 text-sm mt-4">Loading…</div>

    <div
      v-else-if="loadError"
      class="bg-red-50 border border-red-200 rounded-lg px-4 py-3 text-sm text-red-800 mt-4"
    >
      {{ loadError }}
    </div>

    <div v-else-if="definitions.length === 0" class="text-slate-500 text-sm mt-4">
      No export definitions yet.
    </div>

    <table v-else class="w-full text-sm border-collapse">
      <thead>
        <tr class="text-left text-slate-600 border-b border-slate-200">
          <th class="px-3 py-2 font-semibold">Name</th>
          <th class="px-3 py-2 font-semibold">Root table</th>
          <th class="px-3 py-2 font-semibold">Format</th>
          <th class="px-3 py-2 font-semibold">Enabled</th>
          <th class="px-3 py-2 font-semibold">Updated</th>
          <th class="px-3 py-2 font-semibold"></th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="def in definitions" :key="def.id" class="border-b border-slate-100 hover:bg-slate-50">
          <td class="px-3 py-2 font-medium text-slate-800">{{ def.name }}</td>
          <td class="px-3 py-2 font-mono text-slate-700">{{ def.rootTable }}</td>
          <td class="px-3 py-2 text-slate-500">{{ def.outputFormat }}</td>
          <td class="px-3 py-2 text-slate-500">{{ def.isEnabled ? 'Yes' : 'No' }}</td>
          <td class="px-3 py-2 whitespace-nowrap text-slate-500">{{ def.updatedAt ?? def.createdAt }}</td>
          <td class="px-3 py-2 text-right">
            <RouterLink
              :to="{ name: 'export-definition-edit', params: { id: def.id } }"
              class="text-indigo-600 text-sm hover:underline"
            >Edit</RouterLink>
          </td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
