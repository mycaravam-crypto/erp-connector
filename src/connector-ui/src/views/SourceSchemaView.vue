<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { getSourceSchema, type SourceTable } from '@/api/connection'
import SourceColumnsTable from '@/components/SourceColumnsTable.vue'
import { Plug, ChevronRight, ChevronDown, ChevronLeft } from 'lucide-vue-next'
import Icon from '@/components/ui/Icon.vue'
import Button from '@/components/ui/Button.vue'

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
  } catch (err) {
    error.value = err instanceof Error ? err.message : 'Could not reach the API. Is the backend service running?'
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
      <span class="bg-brand text-white px-2.5 py-0.5 rounded-full text-xs font-bold tracking-wide shrink-0">Step 2</span>
      <h1 class="m-0 text-xl font-semibold text-text-primary flex-1">Source Schema</h1>
      <Button variant="secondary" :disabled="loading" @click="load">Refresh</Button>
    </div>

    <p class="text-text-secondary text-sm mt-2 mb-5 leading-relaxed">
      The connector has read the schema from your source database. Review the tables and columns
      below before configuring the export mapping.
    </p>

    <p v-if="loading" class="text-text-secondary">Reading schema…</p>
    <p v-else-if="error" class="text-danger">{{ error }}</p>

    <template v-else-if="schema">
      <div class="flex items-center gap-2 px-3.5 py-2.5 bg-surface-elevated border border-border rounded-md text-sm text-text-secondary mb-5">
        <span class="text-brand"><Icon :icon="Plug" :size="16" /></span>
        <span>{{ schema.connectionLabel }}</span>
        <span class="ml-auto text-xs bg-brand/10 text-brand font-semibold px-2 py-0.5 rounded-full">{{ schema.tables.length }} tables</span>
        <button
          class="text-xs text-text-secondary border border-border-strong rounded px-2 py-0.5 bg-surface cursor-pointer hover:bg-surface-elevated"
          @click="toggleAll"
        >{{ allExpanded ? 'Collapse All' : 'Expand All' }}</button>
      </div>

      <div class="flex flex-col gap-2 mb-6">
        <div v-for="table in schema.tables" :key="table.name" class="border border-border rounded-lg overflow-hidden">
          <button
            class="w-full flex items-center gap-3 px-3.5 py-2.5 bg-surface-elevated border-0 cursor-pointer text-left text-sm hover:bg-border/40"
            @click="toggleTable(table.name)"
          >
            <span class="text-text-secondary shrink-0">
              <Icon :icon="expandedTables.has(table.name) ? ChevronDown : ChevronRight" :size="16" />
            </span>
            <code class="text-sm font-bold text-text-primary shrink-0">{{ table.name }}</code>
            <span class="text-text-secondary text-[0.82rem] flex-1">{{ table.description }}</span>
            <span class="text-xs text-text-muted shrink-0">{{ table.columns.length }} columns</span>
          </button>

          <div v-if="expandedTables.has(table.name)" class="px-2 pb-2 border-t border-border">
            <SourceColumnsTable :columns="table.columns" />
          </div>
        </div>
      </div>

      <div class="flex justify-between mt-4">
        <Button variant="ghost" @click="router.push({ name: 'connect' })">
          <template #icon><Icon :icon="ChevronLeft" :size="16" /></template>
          Back to Connect
        </Button>
        <Button variant="primary" @click="router.push({ name: 'export-schema' })">
          Configure Export Schema
          <Icon :icon="ChevronRight" :size="16" />
        </Button>
      </div>
    </template>
  </div>
</template>
