<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { getSourceSchema, type SourceTable } from '@/api/connection'

const router = useRouter()

const schema = ref<{ connectionLabel: string; tables: SourceTable[] } | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)
const expandedTables = ref<Set<string>>(new Set())

const allExpanded = computed(() =>
  !!schema.value && schema.value.tables.every((t) => expandedTables.value.has(t.name)),
)

function toggleAll() {
  if (allExpanded.value) {
    expandedTables.value = new Set()
  } else {
    expandedTables.value = new Set(schema.value!.tables.map((t) => t.name))
  }
}

async function load() {
  loading.value = true
  error.value = null
  try {
    schema.value = await getSourceSchema()
    if (!schema.value) {
      error.value = 'Source schema endpoint returned no data.'
    }
  } catch {
    error.value = 'Could not reach the API. Is the backend running on :5189?'
  } finally {
    loading.value = false
  }
}

onMounted(load)

function toggleTable(name: string) {
  const next = new Set(expandedTables.value)
  if (next.has(name)) next.delete(name)
  else next.add(name)
  expandedTables.value = next
}
</script>

<template>
  <div class="max-w-3xl">
    <div class="flex items-center gap-3 mb-2">
      <span class="bg-slate-900 text-slate-200 px-2.5 py-0.5 rounded-full text-xs font-bold tracking-wide shrink-0">Step 2</span>
      <h1 class="m-0 text-xl font-semibold flex-1">Source Schema</h1>
      <button
        class="px-3 py-1 border border-slate-300 rounded-md bg-white text-sm text-slate-500 cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed hover:enabled:bg-slate-50"
        :disabled="loading"
        @click="load"
      >Refresh</button>
    </div>

    <p class="text-slate-500 text-sm mt-2 mb-5 leading-relaxed">
      The connector has read the schema from your source database. Review the tables and columns
      below before configuring the export mapping.
    </p>

    <p v-if="loading" class="text-slate-500">Reading schema…</p>
    <p v-else-if="error" class="text-red-600">{{ error }}</p>

    <template v-else-if="schema">
      <div class="flex items-center gap-2 px-3.5 py-2.5 bg-slate-50 border border-slate-200 rounded-md text-sm text-slate-500 mb-5">
        <span class="text-indigo-600 font-bold">⟳</span>
        <span>{{ schema.connectionLabel }}</span>
        <span class="ml-auto text-xs bg-indigo-100 text-indigo-800 font-semibold px-2 py-0.5 rounded-full">{{ schema.tables.length }} tables</span>
        <button
          class="text-xs text-slate-500 border border-slate-300 rounded px-2 py-0.5 bg-white cursor-pointer hover:bg-slate-100"
          @click="toggleAll"
        >{{ allExpanded ? 'Collapse All' : 'Expand All' }}</button>
      </div>

      <div class="flex flex-col gap-2 mb-6">
        <div v-for="table in schema.tables" :key="table.name" class="border border-slate-200 rounded-lg overflow-hidden">
          <button
            class="w-full flex items-center gap-3 px-3.5 py-2.5 bg-slate-50 border-0 cursor-pointer text-left text-sm hover:bg-slate-100"
            @click="toggleTable(table.name)"
          >
            <span class="text-[0.6rem] text-slate-500 shrink-0">{{ expandedTables.has(table.name) ? '▼' : '▶' }}</span>
            <code class="text-sm font-bold text-slate-900 shrink-0">{{ table.name }}</code>
            <span class="text-slate-500 text-[0.82rem] flex-1">{{ table.description }}</span>
            <span class="text-xs text-slate-400 shrink-0">{{ table.columns.length }} columns</span>
          </button>

          <div v-if="expandedTables.has(table.name)" class="px-2 pb-2 border-t border-slate-200">
            <table class="w-full border-collapse text-sm">
              <thead>
                <tr>
                  <th class="px-2.5 py-2 text-left bg-slate-50 font-semibold text-[0.72rem] uppercase tracking-wide text-slate-500 border-b border-slate-100">Column</th>
                  <th class="px-2.5 py-2 text-left bg-slate-50 font-semibold text-[0.72rem] uppercase tracking-wide text-slate-500 border-b border-slate-100">Type</th>
                  <th class="px-2.5 py-2 text-left bg-slate-50 font-semibold text-[0.72rem] uppercase tracking-wide text-slate-500 border-b border-slate-100">Nullable</th>
                  <th class="px-2.5 py-2 text-left bg-slate-50 font-semibold text-[0.72rem] uppercase tracking-wide text-slate-500 border-b border-slate-100">PK</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="col in table.columns" :key="col.name" :class="col.primaryKey ? 'bg-green-50' : ''">
                  <td class="px-2.5 py-1.5 border-b border-slate-50 align-middle">
                    <code class="text-sm text-slate-900">{{ col.name }}</code>
                    <span v-if="col.primaryKey" class="ml-1.5 text-[0.65rem] font-bold bg-blue-100 text-blue-800 px-1.5 py-0.5 rounded">PK</span>
                  </td>
                  <td class="px-2.5 py-1.5 border-b border-slate-50 align-middle">
                    <span class="inline-block px-1.5 py-0.5 bg-slate-100 border border-slate-200 rounded text-xs text-slate-600 whitespace-nowrap">{{ col.type }}</span>
                  </td>
                  <td class="px-2.5 py-1.5 border-b border-slate-50 align-middle">
                    <span :class="col.nullable ? 'text-slate-400 text-xs' : 'text-slate-900 font-semibold text-xs'">
                      {{ col.nullable ? 'YES' : 'NO' }}
                    </span>
                  </td>
                  <td class="px-2.5 py-1.5 border-b border-slate-50 align-middle">
                    <span v-if="col.primaryKey" class="text-blue-700 text-sm">●</span>
                    <span v-else class="text-slate-200">—</span>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>

      <div class="flex justify-between mt-4">
        <button
          class="px-4 py-2 border border-slate-300 rounded-md bg-white text-slate-500 text-sm cursor-pointer hover:bg-slate-50"
          @click="router.push({ name: 'connect' })"
        >← Back to Connect</button>
        <button
          class="px-5 py-2 border-0 rounded-md bg-slate-900 text-slate-200 text-sm font-semibold cursor-pointer hover:bg-slate-800"
          @click="router.push({ name: 'export-schema' })"
        >Configure Export Schema →</button>
      </div>
    </template>
  </div>
</template>
