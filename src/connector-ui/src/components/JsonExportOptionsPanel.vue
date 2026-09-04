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
  <!-- Always visible — this is the primary Step 3 editing surface, not an optional add-on. -->
  <div class="json-export-options">
    <NestedGroupsSection
      :groups="nestedGroups"
      :available-tables="availableTables"
      @add="emit('add')"
      @remove="emit('remove', $event)"
      @dirty="emit('dirty')"
    />
    <JsonEnvelopeEditor v-model="jsonWrapper" @dirty="emit('dirty')" />
  </div>
</template>
