<script setup lang="ts">
import type { SourceTable } from '@/api/connection'
import type { ImportDefinition } from '@/api/importDefinitions'
import HelpTooltip from '@/components/ui/HelpTooltip.vue'

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
      <label class="text-sm text-text-secondary w-36 shrink-0 inline-flex items-center gap-1">
        Root table
        <HelpTooltip label="What's a root table?" title="Root table">
          <p>
            The table an inbound record ultimately updates. The import matches each incoming record
            to one row here using the <strong>Root match column</strong> below, then writes to it.
          </p>
          <p>
            <strong>Example:</strong> pick <code>purchaseorder</code> so a vendor's confirmation JSON
            updates the matching purchase order row's status.
          </p>
          <p>Once you've added fields, this locks — clear them first to switch tables.</p>
        </HelpTooltip>
      </label>
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
      <label class="text-sm text-text-secondary w-36 shrink-0 inline-flex items-center gap-1">
        Root match column
        <HelpTooltip label="What is the root match column?" title="Root match column">
          <p>
            The column in the root table an inbound record's correlation value is compared against to
            find "its" row. This is the receiving end of an export's <strong>Correlation key field</strong>.
          </p>
          <p>
            <strong>Example:</strong> if the paired export tags each record with export key
            <code>guid</code>, set this to the ERP column that holds that same GUID — often
            <code>guid</code> or <code>id</code> — so an inbound reply lands on the right row.
          </p>
        </HelpTooltip>
      </label>
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
      <label class="text-sm text-text-secondary w-36 shrink-0 inline-flex items-center gap-1">
        If unmatched
        <HelpTooltip label="What does Reject vs. Quarantine mean?" title="When an inbound record's correlation key matches no row">
          <ul>
            <li><strong>Reject</strong> — the record is dropped; nothing is written or held for review.</li>
            <li><strong>Quarantine</strong> — the record is held for manual review instead of being discarded, in case it's a timing issue (e.g. the row hasn't been created in the ERP yet).</li>
          </ul>
          <p>Either way, an unmatched record is never used to auto-create a new row.</p>
        </HelpTooltip>
      </label>
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
    <!-- knowledge/pipeline/import-mapping-presets.md §6 Open Decision #2 — read-only, for later auditing
         of why this definition's tree looks the way it does. Only set via "Create from export"; not
         editable by hand here. -->
    <div v-if="definition.integrationKey" class="flex items-center gap-2">
      <label class="text-sm text-text-secondary w-36 shrink-0">Paired with</label>
      <span class="text-sm text-text-secondary font-mono">{{ definition.integrationKey }} v{{ definition.contractVersion }}</span>
    </div>
  </div>
</template>
