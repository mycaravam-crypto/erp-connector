<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { listErpRecords, type ErpCiRecord } from '@/api/erp'
import CiDetailPanel from '@/components/CiDetailPanel.vue'

const records = ref<ErpCiRecord[]>([])
const totalCount = ref(0)
const loading = ref(true)
const error = ref<string | null>(null)

// Search and filter state
const search = ref('')
const scopeFilter = ref<'all' | 'in-scope' | 'excluded'>('all')
const expandedIds = ref<Set<string>>(new Set())
const detailId = ref<string | null>(null)

onMounted(async () => {
  try {
    const result = await listErpRecords()
    records.value = result.records
    totalCount.value = result.total
  } catch {
    error.value = 'Could not reach the API. Is the backend service running?'
  } finally {
    loading.value = false
  }
})

// Flat-list mode activates when search or scope filter is active
const isFlatMode = computed(() => search.value.trim() !== '' || scopeFilter.value !== 'all')

const filtered = computed(() => {
  let list = records.value

  if (scopeFilter.value === 'in-scope') list = list.filter((r) => r.inScope)
  else if (scopeFilter.value === 'excluded') list = list.filter((r) => !r.inScope)

  const q = search.value.trim().toLowerCase()
  if (q) {
    list = list.filter((r) =>
      [r.serial, r.articleName, r.partNumber, r.manufacturer, r.id]
        .some((v) => v?.toLowerCase().includes(q)),
    )
  }

  return list
})

// BOM tree: roots are records with no parentId (among visible records)
const visibleIds = computed(() => new Set(filtered.value.map((r) => r.id)))
const roots = computed(() =>
  filtered.value.filter((r) => !r.parentId || !visibleIds.value.has(r.parentId)),
)
const childrenOf = computed(() => {
  const map = new Map<string, ErpCiRecord[]>()
  for (const r of filtered.value) {
    if (r.parentId && visibleIds.value.has(r.parentId)) {
      const list = map.get(r.parentId) ?? []
      list.push(r)
      map.set(r.parentId, list)
    }
  }
  return map
})

function toggleExpand(id: string) {
  if (expandedIds.value.has(id)) expandedIds.value.delete(id)
  else expandedIds.value.add(id)
  // Re-assign to trigger reactivity
  expandedIds.value = new Set(expandedIds.value)
}

function toggleDetail(id: string) {
  detailId.value = detailId.value === id ? null : id
}

function detailFor(id: string): ErpCiRecord | undefined {
  return records.value.find((r) => r.id === id)
}

const inScopeCount = computed(() => records.value.filter((r) => r.inScope).length)
const excludedCount = computed(() => records.value.filter((r) => !r.inScope).length)

// Sorting for flat list
type SortKey = 'serial' | 'articleName' | 'partNumber' | 'manufacturer' | 'status' | 'inScope'
const sortKey = ref<SortKey>('serial')
const sortAsc = ref(true)

function setSort(key: SortKey) {
  if (sortKey.value === key) sortAsc.value = !sortAsc.value
  else { sortKey.value = key; sortAsc.value = true }
}

function sortIcon(key: SortKey) {
  if (sortKey.value !== key) return '↕'
  return sortAsc.value ? '↑' : '↓'
}

const sortedFlat = computed(() => {
  const list = [...filtered.value]
  const dir = sortAsc.value ? 1 : -1
  list.sort((a, b) => {
    const av = String(a[sortKey.value] ?? '')
    const bv = String(b[sortKey.value] ?? '')
    return av.localeCompare(bv) * dir
  })
  return list
})
</script>

<template>
  <div>
    <div class="flex items-center gap-3 mb-2">
      <h1 class="m-0 text-xl font-semibold">ERP Database</h1>
      <span v-if="!loading && !error" class="text-xs text-slate-500">
        {{ totalCount }} CIs total ·
        <span class="text-green-700">{{ inScopeCount }} in scope</span> ·
        <span class="text-slate-500">{{ excludedCount }} excluded</span>
      </span>
    </div>

    <p class="text-sm text-slate-500 mb-4">
      Live view of the ERP database — shows every CI, its scope status, and the excluded fields
      that confirm the data-minimisation boundary.
    </p>

    <!-- Cap notice: shown when the API returned fewer records than the total -->
    <div
      v-if="!loading && !error && records.length < totalCount"
      class="bg-amber-50 border border-amber-200 rounded-md px-4 py-2.5 text-sm text-amber-900 mb-4"
    >
      Showing {{ records.length }} of {{ totalCount }} CIs — the view is capped at 500.
      Search or filter to narrow results within the loaded set.
    </div>

    <!-- Controls -->
    <div v-if="!loading && !error" class="flex flex-wrap items-center gap-3 mb-4">
      <input
        v-model="search"
        type="search"
        placeholder="Search serial, model, part, manufacturer, ID…"
        class="border border-slate-300 rounded-md px-3 py-1.5 text-sm w-72 focus:outline-none focus:ring-2 focus:ring-indigo-400"
      />
      <div class="flex rounded-md border border-slate-300 overflow-hidden text-sm">
        <button
          v-for="opt in [['all', 'All'], ['in-scope', 'In Scope'], ['excluded', 'Excluded']] as const"
          :key="opt[0]"
          class="px-3 py-1.5 transition-colors"
          :class="scopeFilter === opt[0]
            ? 'bg-indigo-600 text-white'
            : 'bg-white text-slate-600 hover:bg-slate-50'"
          @click="scopeFilter = opt[0]"
        >
          {{ opt[1] }}
        </button>
      </div>
      <span v-if="isFlatMode" class="text-xs text-slate-400">Flat list mode · {{ filtered.length }} results</span>
    </div>

    <div v-if="loading" class="text-slate-500 text-sm">Loading ERP records…</div>
    <div v-else-if="error" class="bg-red-50 border border-red-200 rounded-lg px-4 py-3 text-sm text-red-800">{{ error }}</div>
    <div v-else-if="filtered.length === 0" class="text-slate-500 text-sm">No records match the current filter.</div>

    <!-- ── Flat list mode ─────────────────────────────────────────────────────── -->
    <template v-else-if="isFlatMode">
      <div class="rounded-lg border border-slate-200 overflow-hidden text-sm">
        <table class="w-full">
          <thead>
            <tr class="bg-slate-50 border-b border-slate-200">
              <th
                v-for="[key, label] in [['serial','Serial'], ['articleName','Model'], ['partNumber','Part #'], ['manufacturer','Manufacturer'], ['status','Status'], ['inScope','Scope']]"
                :key="key"
                class="text-left px-3 py-2 text-slate-600 font-medium cursor-pointer select-none hover:bg-slate-100"
                @click="setSort(key as SortKey)"
              >
                {{ label }} <span class="text-slate-400 text-xs">{{ sortIcon(key as SortKey) }}</span>
              </th>
            </tr>
          </thead>
          <tbody>
            <template v-for="r in sortedFlat" :key="r.id">
              <tr
                class="border-b border-slate-100 last:border-0 cursor-pointer hover:bg-slate-50 transition-colors"
                :class="detailId === r.id ? 'bg-indigo-50' : ''"
                @click="toggleDetail(r.id)"
              >
                <td class="px-3 py-2 font-mono text-xs">{{ r.serial ?? '—' }}</td>
                <td class="px-3 py-2">{{ r.articleName ?? '—' }}</td>
                <td class="px-3 py-2 font-mono text-xs">{{ r.partNumber ?? '—' }}</td>
                <td class="px-3 py-2">{{ r.manufacturer ?? '—' }}</td>
                <td class="px-3 py-2">{{ r.status ?? '—' }}</td>
                <td class="px-3 py-2">
                  <span
                    class="inline-flex items-center text-xs font-medium px-2 py-0.5 rounded-full"
                    :class="r.inScope ? 'bg-green-100 text-green-800' : 'bg-slate-100 text-slate-500'"
                  >
                    {{ r.inScope ? 'In Scope' : r.exclusionReason ?? 'Excluded' }}
                  </span>
                </td>
              </tr>
              <!-- Detail panel -->
              <tr v-if="detailId === r.id" class="bg-indigo-50 border-b border-slate-100">
                <td colspan="6" class="px-4 py-3">
                  <CiDetailPanel :record="r" show-parent-serial />
                </td>
              </tr>
            </template>
          </tbody>
        </table>
      </div>
    </template>

    <!-- ── BOM tree mode ──────────────────────────────────────────────────────── -->
    <template v-else>
      <div class="rounded-lg border border-slate-200 overflow-hidden text-sm">
        <table class="w-full">
          <thead>
            <tr class="bg-slate-50 border-b border-slate-200">
              <th class="text-left px-3 py-2 text-slate-600 font-medium">Serial / Model</th>
              <th class="text-left px-3 py-2 text-slate-600 font-medium">Part #</th>
              <th class="text-left px-3 py-2 text-slate-600 font-medium">Status</th>
              <th class="text-left px-3 py-2 text-slate-600 font-medium">Scope</th>
            </tr>
          </thead>
          <tbody>
            <template v-for="root in roots" :key="root.id">
              <!-- Root row -->
              <tr
                class="border-b border-slate-100 cursor-pointer hover:bg-slate-50 transition-colors"
                :class="detailId === root.id ? 'bg-indigo-50' : ''"
                @click="toggleDetail(root.id)"
              >
                <td class="px-3 py-2">
                  <div class="flex items-center gap-2">
                    <button
                      v-if="childrenOf.get(root.id)?.length"
                      class="w-5 h-5 flex items-center justify-center border border-slate-300 rounded text-slate-500 text-xs bg-white hover:bg-slate-100 shrink-0"
                      @click.stop="toggleExpand(root.id)"
                    >
                      {{ expandedIds.has(root.id) ? '−' : '+' }}
                    </button>
                    <span v-else class="w-5 shrink-0 text-center text-slate-300 text-xs select-none">·</span>
                    <div>
                      <span class="font-mono text-xs">{{ root.serial ?? '—' }}</span>
                      <span v-if="root.articleName" class="text-slate-500 text-xs ml-1.5">{{ root.articleName }}</span>
                    </div>
                  </div>
                </td>
                <td class="px-3 py-2 font-mono text-xs text-slate-500">{{ root.partNumber ?? '—' }}</td>
                <td class="px-3 py-2 text-xs">{{ root.status ?? '—' }}</td>
                <td class="px-3 py-2">
                  <span
                    class="inline-flex items-center text-xs font-medium px-2 py-0.5 rounded-full"
                    :class="root.inScope ? 'bg-green-100 text-green-800' : 'bg-slate-100 text-slate-500'"
                  >
                    {{ root.inScope ? 'In Scope' : root.exclusionReason ?? 'Excluded' }}
                  </span>
                </td>
              </tr>

              <!-- Root detail panel -->
              <tr v-if="detailId === root.id" class="bg-indigo-50 border-b border-slate-100">
                <td colspan="4" class="px-4 py-3">
                  <CiDetailPanel :record="root" />
                </td>
              </tr>

              <!-- Children -->
              <template v-if="expandedIds.has(root.id)">
                <template v-for="child in childrenOf.get(root.id)" :key="child.id">
                  <tr
                    class="border-b border-slate-100 cursor-pointer hover:bg-slate-50 transition-colors"
                    :class="detailId === child.id ? 'bg-indigo-50' : ''"
                    @click="toggleDetail(child.id)"
                  >
                    <td class="pl-8 pr-3 py-2">
                      <div class="flex items-center gap-2">
                        <span class="text-slate-300 text-xs select-none">└</span>
                        <div>
                          <span class="font-mono text-xs">{{ child.serial ?? '—' }}</span>
                          <span v-if="child.articleName" class="text-slate-500 text-xs ml-1.5">{{ child.articleName }}</span>
                        </div>
                      </div>
                    </td>
                    <td class="px-3 py-2 font-mono text-xs text-slate-500">{{ child.partNumber ?? '—' }}</td>
                    <td class="px-3 py-2 text-xs">{{ child.status ?? '—' }}</td>
                    <td class="px-3 py-2">
                      <span
                        class="inline-flex items-center text-xs font-medium px-2 py-0.5 rounded-full"
                        :class="child.inScope ? 'bg-green-100 text-green-800' : 'bg-slate-100 text-slate-500'"
                      >
                        {{ child.inScope ? 'In Scope' : child.exclusionReason ?? 'Excluded' }}
                      </span>
                    </td>
                  </tr>

                  <!-- Child detail panel -->
                  <tr v-if="detailId === child.id" class="bg-indigo-50 border-b border-slate-100">
                    <td colspan="4" class="pl-12 pr-4 py-3">
                      <CiDetailPanel :record="child" />
                    </td>
                  </tr>
                </template>
              </template>

            </template>
          </tbody>
        </table>
      </div>
    </template>
  </div>
</template>
