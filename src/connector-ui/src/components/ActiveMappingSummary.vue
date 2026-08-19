<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink } from 'vue-router'
import type { ExportMappingConfig } from '@/api/mapping'

const props = defineProps<{
  mapping: ExportMappingConfig | null
  loading: boolean
}>()

const enabledFields = computed(() => props.mapping?.fields.filter((f) => f.enabled) ?? [])
const enabledRelations = computed(() => props.mapping?.relations.filter((r) => r.enabled) ?? [])
const enabledNestedGroups = computed(() => props.mapping?.nestedGroups?.filter((g) => g.enabled) ?? [])
const enabledRelationFields = computed(() =>
  enabledRelations.value.flatMap((r) =>
    (r.fields ?? []).filter((f) => f.enabled).map((f) => ({ relatedTable: r.relatedTable, ...f })),
  ),
)
</script>

<template>
  <div v-if="!loading" class="bg-slate-50 border border-slate-200 rounded-lg px-5 py-4 mb-8">
    <div class="flex items-center gap-3 mb-1">
      <h2 class="m-0 text-base font-semibold text-slate-900">Active Mapping</h2>
      <span v-if="mapping" class="inline-block px-2 py-0.5 rounded-full text-xs font-bold uppercase tracking-wide bg-green-100 text-green-700">Live Postgres</span>
      <span v-else class="inline-block px-2 py-0.5 rounded-full text-xs font-bold uppercase tracking-wide bg-slate-100 text-slate-500">Not configured</span>
      <RouterLink v-if="mapping" :to="{ name: 'export-schema' }" class="ml-auto px-2.5 py-1 border border-slate-300 rounded-md bg-white text-xs text-slate-500 no-underline hover:bg-slate-50">Edit in Step 3 →</RouterLink>
      <RouterLink v-else :to="{ name: 'export-schema' }" class="ml-auto px-2.5 py-1 border border-slate-300 rounded-md bg-white text-xs text-slate-500 no-underline hover:bg-slate-50">Configure in Step 3 →</RouterLink>
    </div>

    <div v-if="mapping">
      <p class="text-sm text-slate-500 m-0 mb-2">
        Source table: <strong class="text-slate-900">{{ mapping.sourceTable }}</strong>
        &nbsp;·&nbsp;{{ enabledFields.length }} field<span v-if="enabledFields.length !== 1">s</span>
        <template v-if="enabledRelations.length > 0">
          &nbsp;+&nbsp;{{ enabledRelations.length }} relation<span v-if="enabledRelations.length !== 1">s</span>
        </template>
        <template v-if="enabledNestedGroups.length > 0">
          &nbsp;+&nbsp;{{ enabledNestedGroups.length }} nested group<span v-if="enabledNestedGroups.length !== 1">s</span>
          <span class="text-slate-400">(JSON only)</span>
        </template>
      </p>
      <div class="flex flex-wrap gap-1.5">
        <span v-for="f in enabledFields" :key="f.sourceName" class="inline-flex items-center gap-1 bg-white border border-slate-300 rounded-full px-2.5 py-0.5 text-xs">
          <span class="text-slate-500">{{ f.sourceName }}</span>
          <template v-if="f.targetName !== f.sourceName">
            <span class="text-slate-300 text-xs">→</span>
            <span class="text-slate-900 font-semibold">{{ f.targetName }}</span>
          </template>
        </span>
        <span v-for="f in enabledRelationFields" :key="`${f.relatedTable}.${f.sourceField}`" class="inline-flex items-center gap-1 bg-indigo-50 border border-indigo-200 rounded-full px-2.5 py-0.5 text-xs">
          <span class="text-slate-500">{{ f.relatedTable }}.{{ f.sourceField }}</span>
          <span class="text-slate-300 text-xs">→</span>
          <span class="text-slate-900 font-semibold">{{ f.targetField }}</span>
        </span>
      </div>
    </div>
    <p v-else class="text-sm text-slate-500 m-0">
      No export mapping saved yet. Configure one in Step 3 before running an export.
    </p>
  </div>
</template>
