<script setup lang="ts">
import { ref, computed } from 'vue'
import { X } from 'lucide-vue-next'
import Icon from '@/components/ui/Icon.vue'
import SectionHeader from '@/components/ui/SectionHeader.vue'
import HelpTooltip from '@/components/ui/HelpTooltip.vue'

// This is the primary safety boundary of the whole import feature (import-definitions.md §1, Open
// Decision #9): a saved mapping can never write outside this explicit allowlist, checked against the
// live schema at save time. Deliberately its own component, separate from the tree, so it's never
// something an operator can miss while scrolling past field rows — the acceptance criteria for this
// slice require it to be "visibly and separately editable from the tree itself."
const props = defineProps<{
  columns: string[]
  /** TargetColumn values the tree currently references (excluding the root match field) — used only
   * to flag ones missing from the allowlist below; the tree itself is never edited from here. */
  usedColumns: string[]
}>()
const emit = defineEmits<{ dirty: [] }>()

const newColumn = ref('')

function add() {
  const value = newColumn.value.trim()
  if (value === '' || props.columns.includes(value)) return
  props.columns.push(value)
  newColumn.value = ''
  emit('dirty')
}

function remove(idx: number) {
  props.columns.splice(idx, 1)
  emit('dirty')
}

const missing = computed(() => props.usedColumns.filter((c) => !props.columns.includes(c)))
</script>

<template>
  <div class="mb-6 border-2 border-warning bg-warning-bg rounded-lg px-4 py-3">
    <SectionHeader title="Allowed Writable Columns" class="mb-1">
      <template #help>
        <HelpTooltip label="Why does this list exist?" title="A safety net, separate from the field tree">
          <p>
            This is a hard allowlist checked at save time — independent of the field tree below, so a
            mistake in the tree can never silently widen what this import is allowed to touch.
          </p>
          <p>
            <strong>Example:</strong> even if the tree maps a field to <code>price</code>, saving fails
            unless <code>price</code> is also listed here. Add each column you genuinely want inbound
            data to be able to overwrite, e.g. <code>status</code>, <code>notes</code>.
          </p>
        </HelpTooltip>
      </template>
    </SectionHeader>
    <p class="text-sm text-text-secondary mt-0 mb-3 leading-relaxed">
      The only columns this definition may ever write. A tree field targeting a column not listed here
      is rejected at save time, even if it looks correctly wired up in the tree above.
    </p>

    <div class="flex flex-wrap gap-2 mb-3">
      <span v-if="columns.length === 0" class="text-sm text-text-muted">No columns allowed yet — nothing can be written.</span>
      <span
        v-for="(col, idx) in columns"
        :key="col"
        class="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full bg-surface border border-warning text-sm font-mono text-text-primary"
      >
        {{ col }}
        <button
          type="button"
          class="p-0 border-0 bg-transparent cursor-pointer text-text-muted hover:text-danger"
          :aria-label="`Remove ${col}`"
          @click="remove(idx)"
        ><Icon :icon="X" :size="16" /></button>
      </span>
    </div>

    <div class="flex gap-2 items-center">
      <input
        type="text"
        v-model="newColumn"
        placeholder="e.g. status"
        aria-label="Add allowed column"
        class="px-2.5 py-1.5 border border-border-strong rounded-md text-sm font-mono text-text-primary outline-none focus:border-brand"
        @keyup.enter="add"
      />
      <button
        type="button"
        class="px-3 py-1.5 border border-border-strong rounded-md bg-surface text-sm text-text-primary cursor-pointer hover:bg-surface-elevated"
        @click="add"
      >+ Add</button>
    </div>

    <p v-if="missing.length > 0" class="text-sm text-danger mt-3 mb-0">
      The tree writes to {{ missing.length === 1 ? 'a column' : 'columns' }} not in this allowlist:
      <code>{{ missing.join(', ') }}</code> — add {{ missing.length === 1 ? 'it' : 'them' }} above or the
      save will be rejected.
    </p>
  </div>
</template>
