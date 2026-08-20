<script setup lang="ts">
import type { SourceTable } from '@/api/connection'
import type { MappingNestedGroup, ExportJsonWrapperConfig } from '@/api/mapping'
import NestedGroupsSection from '@/components/NestedGroupsSection.vue'
import JsonEnvelopeEditor from '@/components/JsonEnvelopeEditor.vue'

defineProps<{
  nestedGroups: MappingNestedGroup[]
  availableTables: SourceTable[]
}>()

const jsonWrapper = defineModel<ExportJsonWrapperConfig | null>('jsonWrapper', { required: true })

const emit = defineEmits<{
  add: []
  remove: [idx: number]
  dirty: []
}>()
</script>

<template>
  <!-- Collapsed unless already configured -->
  <details class="mb-7" :open="nestedGroups.length > 0 || jsonWrapper !== null">
    <summary class="cursor-pointer select-none text-base font-semibold text-slate-900">
      JSON Export Options <span class="text-xs font-normal text-slate-400">(optional)</span>
    </summary>
    <div class="mt-3">
      <NestedGroupsSection
        :groups="nestedGroups"
        :available-tables="availableTables"
        @add="emit('add')"
        @remove="emit('remove', $event)"
        @dirty="emit('dirty')"
      />
      <JsonEnvelopeEditor v-model="jsonWrapper" @dirty="emit('dirty')" />
    </div>
  </details>
</template>
