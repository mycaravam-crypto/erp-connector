<script setup lang="ts">
import type { ExportNode } from '@/api/exportDefinitions'

// Self-referencing recursive component (see NestedGroupEditor.vue for the same pattern applied to
// the legacy mapping shape). Deliberately a flat, read-the-identifiers-and-fix-them form — not a
// tree builder: it edits SourceField/RelatedTable/JoinKey/SourceJoinKey and the Enabled flag on the
// existing tree in place, but never adds, removes, or re-kinds a node. That's enough to point a
// migrated definition at real production table/column names; building/restructuring the tree is
// export-definitions-2.0.md's still-unbuilt Slice 5 tree editor, not this recovery form.
defineOptions({ name: 'ExportNodeFieldEditor' })

defineProps<{
  nodes: ExportNode[]
  depth: number
}>()

defineEmits<{
  dirty: []
}>()
</script>

<template>
  <div class="flex flex-col gap-2">
    <div
      v-for="node in nodes"
      :key="node.targetKey"
      class="border border-slate-200 rounded-md px-3 py-2 bg-white"
      :style="{ marginLeft: `${depth * 1.25}rem` }"
    >
      <div class="flex items-center gap-2 mb-2">
        <input type="checkbox" v-model="node.enabled" class="cursor-pointer" @change="$emit('dirty')" />
        <code class="text-sm font-bold text-slate-900">{{ node.targetKey }}</code>
        <span class="text-xs bg-slate-100 text-slate-500 px-1.5 py-0.5 rounded-full">{{ node.kind }}</span>
      </div>

      <div v-if="node.kind === 'scalar-field'" class="flex items-center gap-2 text-sm">
        <label class="text-slate-500 w-28 shrink-0">Source column</label>
        <input
          type="text"
          v-model="node.sourceField"
          placeholder="table.column or column"
          class="flex-1 px-2 py-1 border border-slate-300 rounded text-sm text-slate-900 font-mono outline-none focus:border-slate-900"
          @input="$emit('dirty')"
        />
      </div>

      <div v-else class="flex flex-col gap-1.5 text-sm">
        <div class="flex items-center gap-2">
          <label class="text-slate-500 w-28 shrink-0">Related table</label>
          <input
            type="text"
            v-model="node.relatedTable"
            class="flex-1 px-2 py-1 border border-slate-300 rounded text-sm text-slate-900 font-mono outline-none focus:border-slate-900"
            @input="$emit('dirty')"
          />
        </div>
        <div class="flex items-center gap-2">
          <label class="text-slate-500 w-28 shrink-0">Join key</label>
          <input
            type="text"
            v-model="node.joinKey"
            class="flex-1 px-2 py-1 border border-slate-300 rounded text-sm text-slate-900 font-mono outline-none focus:border-slate-900"
            @input="$emit('dirty')"
          />
        </div>
        <div class="flex items-center gap-2">
          <label class="text-slate-500 w-28 shrink-0">Source join key</label>
          <input
            type="text"
            v-model="node.sourceJoinKey"
            class="flex-1 px-2 py-1 border border-slate-300 rounded text-sm text-slate-900 font-mono outline-none focus:border-slate-900"
            @input="$emit('dirty')"
          />
        </div>

        <ExportNodeFieldEditor
          v-if="node.children.length > 0"
          :nodes="node.children"
          :depth="depth + 1"
          class="mt-1"
          @dirty="$emit('dirty')"
        />
      </div>
    </div>
  </div>
</template>
