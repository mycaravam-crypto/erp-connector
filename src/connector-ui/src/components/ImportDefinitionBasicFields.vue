<script setup lang="ts">
import type { SourceTable } from '@/api/connection'
import type { ImportDefinition } from '@/api/importDefinitions'

const props = defineProps<{
  definition: ImportDefinition
  availableTables: SourceTable[]
  /** Disables the root-table picker once children reference it — changing tables out from under an
   * already-built tree would silently invalidate every TargetColumn/RelatedTable in it. */
  rootTableLocked: boolean
}>()
const emit = defineEmits<{ 'root-table-changed': [] }>()

function onRootTableChanged() {
  if (!props.rootTableLocked) emit('root-table-changed')
}
</script>

<template>
  <div class="flex flex-col gap-3 mb-5">
    <div class="flex items-center gap-2">
      <label class="text-sm text-text-secondary w-36 shrink-0">Name</label>
      <input
        type="text"
        v-model="definition.name"
        aria-label="Name"
        class="flex-1 px-2.5 py-1.5 border border-border-strong rounded-md text-sm text-text-primary outline-none focus:border-brand"
      />
    </div>
    <div class="flex items-center gap-2">
      <label class="text-sm text-text-secondary w-36 shrink-0">Description</label>
      <input
        type="text"
        v-model="definition.description"
        placeholder="optional"
        aria-label="Description"
        class="flex-1 px-2.5 py-1.5 border border-border-strong rounded-md text-sm text-text-primary outline-none focus:border-brand"
      />
    </div>
    <div class="flex items-center gap-2">
      <label class="text-sm text-text-secondary w-36 shrink-0">Root table</label>
      <select
        v-if="availableTables.length > 0"
        v-model="definition.rootTable"
        :disabled="rootTableLocked"
        aria-label="Root table"
        class="flex-1 px-2.5 py-1.5 border border-border-strong rounded-md text-sm text-text-primary font-mono bg-surface disabled:bg-surface-elevated disabled:text-text-muted"
        @change="onRootTableChanged"
      >
        <option value="" disabled>— select —</option>
        <option v-for="t in availableTables" :key="t.name" :value="t.name">{{ t.name }}</option>
      </select>
      <input
        v-else
        type="text"
        v-model="definition.rootTable"
        placeholder="e.g. masterdata"
        aria-label="Root table"
        class="flex-1 px-2.5 py-1.5 border border-border-strong rounded-md text-sm text-text-primary font-mono outline-none focus:border-brand"
      />
      <span v-if="rootTableLocked" class="text-xs text-text-muted">clear all fields to change</span>
    </div>
    <div class="flex items-center gap-2">
      <label class="text-sm text-text-secondary w-36 shrink-0">Root match column</label>
      <input
        type="text"
        v-model="definition.rootMatchColumn"
        placeholder="e.g. guid"
        aria-label="Root match column"
        class="flex-1 px-2.5 py-1.5 border border-border-strong rounded-md text-sm text-text-primary font-mono outline-none focus:border-brand"
      />
      <span class="text-xs text-text-muted">correlation key an inbound record must match to an existing row</span>
    </div>
    <div class="flex items-center gap-2">
      <label class="text-sm text-text-secondary w-36 shrink-0">If unmatched</label>
      <select
        v-model="definition.unmatchedRootPolicy"
        aria-label="Unmatched root policy"
        class="px-2.5 py-1.5 border border-border-strong rounded-md text-sm text-text-primary bg-surface"
      >
        <option value="reject">Reject</option>
        <option value="quarantine">Quarantine</option>
      </select>
      <span class="text-xs text-text-muted">a record whose correlation key matches no row is never auto-created</span>
    </div>
    <div class="flex items-center gap-2">
      <label class="text-sm text-text-secondary w-36 shrink-0">Enabled</label>
      <input type="checkbox" v-model="definition.isEnabled" class="cursor-pointer" />
      <span class="text-xs text-text-secondary">The inbound folder watcher only stages files against enabled definitions.</span>
    </div>
  </div>
</template>
