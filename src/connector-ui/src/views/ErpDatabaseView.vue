<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { listErpRecords, type ErpCiRecord } from '@/api/erp'

// ── state ──────────────────────────────────────────────────────────────────────
const records = ref<ErpCiRecord[]>([])
const error = ref<string | null>(null)
const loading = ref(true)
const scopeFilter = ref<'all' | 'in-scope' | 'excluded'>('all')
const search = ref('')
const sortCol = ref<string | null>(null)
const sortAsc = ref(true)
const expandedIds = ref<Set<string>>(new Set())
const detailId = ref<string | null>(null)

// ── load ───────────────────────────────────────────────────────────────────────
async function load() {
  loading.value = true
  error.value = null
  try {
    const data = await listErpRecords()
    records.value = data
    const allIds = new Set(data.map((r) => r.id))
    const next = new Set<string>()
    for (const r of data) {
      if (!r.parentId || !allIds.has(r.parentId)) next.add(r.id)
    }
    expandedIds.value = next
  } catch {
    error.value = 'Could not reach the API. Is the backend running on :5189?'
  } finally {
    loading.value = false
  }
}

onMounted(load)

// ── counts ─────────────────────────────────────────────────────────────────────
const inScopeCount = computed(() => records.value.filter((r) => r.inScope).length)
const excludedCount = computed(() => records.value.filter((r) => !r.inScope).length)

// Tree mode: only when no search and scope is "all"
const isTreeMode = computed(() => scopeFilter.value === 'all' && !search.value.trim())

// ── filtering & sorting ────────────────────────────────────────────────────────
function matchesFilter(r: ErpCiRecord): boolean {
  if (scopeFilter.value === 'in-scope' && !r.inScope) return false
  if (scopeFilter.value === 'excluded' && r.inScope) return false
  const q = search.value.toLowerCase().trim()
  if (q) {
    const haystack = [r.serial, r.articleName, r.partNumber, r.manufacturer, r.id]
    if (!haystack.some((v) => v?.toLowerCase().includes(q))) return false
  }
  return true
}

function applySort(list: ErpCiRecord[]): ErpCiRecord[] {
  if (!sortCol.value) return list
  const col = sortCol.value
  return [...list].sort((a, b) => {
    const av = (a as Record<string, unknown>)[col] as string | null ?? ''
    const bv = (b as Record<string, unknown>)[col] as string | null ?? ''
    return sortAsc.value ? av.localeCompare(bv) : bv.localeCompare(av)
  })
}

// ── tree builder ───────────────────────────────────────────────────────────────
interface DisplayRow {
  record: ErpCiRecord
  depth: number
  hasChildren: boolean
}

function buildTree(): DisplayRow[] {
  const allIds = new Set(records.value.map((r) => r.id))
  const childrenOf = new Map<string, ErpCiRecord[]>()
  for (const r of records.value) {
    if (r.parentId && allIds.has(r.parentId)) {
      if (!childrenOf.has(r.parentId)) childrenOf.set(r.parentId, [])
      childrenOf.get(r.parentId)!.push(r)
    }
  }

  const rows: DisplayRow[] = []

  function walk(r: ErpCiRecord, depth: number) {
    const children = childrenOf.get(r.id) ?? []
    rows.push({ record: r, depth, hasChildren: children.length > 0 })
    if (expandedIds.value.has(r.id)) {
      for (const child of children) walk(child, depth + 1)
    }
  }

  const roots = records.value.filter((r) => !r.parentId || !allIds.has(r.parentId))
  for (const root of roots) walk(root, 0)
  return rows
}

const displayRows = computed((): DisplayRow[] => {
  if (isTreeMode.value) return buildTree()
  const flat = applySort(records.value.filter(matchesFilter))
  return flat.map((r) => ({ record: r, depth: 0, hasChildren: false }))
})

// ── actions ────────────────────────────────────────────────────────────────────
function toggleExpand(id: string) {
  const next = new Set(expandedIds.value)
  if (next.has(id)) next.delete(id)
  else next.add(id)
  expandedIds.value = next
}

function toggleDetail(id: string) {
  detailId.value = detailId.value === id ? null : id
}

function setSort(col: string) {
  if (sortCol.value === col) sortAsc.value = !sortAsc.value
  else { sortCol.value = col; sortAsc.value = true }
}

function sortIcon(col: string): string {
  if (sortCol.value !== col) return '↕'
  return sortAsc.value ? '↑' : '↓'
}

function planBadgeClass(status: string | null): string {
  if (status === 'Active') return 'badge-plan-active'
  if (status) return 'badge-plan-inactive'
  return 'badge-plan-none'
}
</script>

<template>
  <div>
    <div class="toolbar">
      <h1>ERP Demo Database</h1>
      <input
        v-model="search"
        type="search"
        placeholder="Search serial, model, part #…"
        class="search-box"
      />
      <button @click="load" :disabled="loading">Refresh</button>
    </div>

    <p v-if="loading" class="info">Loading…</p>
    <p v-else-if="error" class="error">{{ error }}</p>

    <template v-else>
      <div class="summary-bar">
        <span class="pill pill-green">{{ inScopeCount }} in scope</span>
        <span class="pill pill-red">{{ excludedCount }} excluded</span>
        <span class="total">{{ records.length }} total</span>
        <span class="mode-tag" v-if="isTreeMode">BOM tree</span>
        <span class="mode-tag mode-flat" v-else>flat list</span>
      </div>

      <div class="filter-row">
        <button
          v-for="opt in [
            { value: 'all', label: 'All' },
            { value: 'in-scope', label: 'In Scope' },
            { value: 'excluded', label: 'Excluded' },
          ]"
          :key="opt.value"
          :class="['filter-btn', scopeFilter === opt.value ? 'active' : '']"
          @click="scopeFilter = opt.value as 'all' | 'in-scope' | 'excluded'"
        >
          {{ opt.label }}
        </button>
      </div>

      <table>
        <thead>
          <tr>
            <th class="col-scope">Scope</th>
            <th class="col-id">
              <span v-if="isTreeMode">ID</span>
              <button v-else class="sort-btn" @click="setSort('id')">
                ID <span class="sort-icon" :class="{ active: sortCol === 'id' }">{{ sortIcon('id') }}</span>
              </button>
            </th>
            <th>
              <span v-if="isTreeMode">Serial</span>
              <button v-else class="sort-btn" @click="setSort('serial')">
                Serial <span class="sort-icon" :class="{ active: sortCol === 'serial' }">{{ sortIcon('serial') }}</span>
              </button>
            </th>
            <th>
              <span v-if="isTreeMode">Model</span>
              <button v-else class="sort-btn" @click="setSort('articleName')">
                Model <span class="sort-icon" :class="{ active: sortCol === 'articleName' }">{{ sortIcon('articleName') }}</span>
              </button>
            </th>
            <th>Part #</th>
            <th>
              <span v-if="isTreeMode">CI Status</span>
              <button v-else class="sort-btn" @click="setSort('status')">
                CI Status <span class="sort-icon" :class="{ active: sortCol === 'status' }">{{ sortIcon('status') }}</span>
              </button>
            </th>
            <th>
              <span v-if="isTreeMode">Commissioned</span>
              <button v-else class="sort-btn" @click="setSort('commissionDate')">
                Commissioned <span class="sort-icon" :class="{ active: sortCol === 'commissionDate' }">{{ sortIcon('commissionDate') }}</span>
              </button>
            </th>
            <th>Maintenance Plan</th>
            <th>Parent</th>
          </tr>
        </thead>
        <tbody>
          <template v-for="row in displayRows" :key="row.record.id">
            <!-- Data row -->
            <tr
              :class="[
                row.record.inScope ? 'row-in-scope' : 'row-excluded',
                detailId === row.record.id ? 'row-selected' : '',
                'data-row',
              ]"
              @click="toggleDetail(row.record.id)"
            >
              <td>
                <span v-if="row.record.inScope" class="badge badge-inscope">In Scope</span>
                <span v-else class="badge badge-excluded" :title="row.record.exclusionReason ?? ''">
                  Excluded
                </span>
              </td>
              <td class="id-col">
                <span
                  class="tree-indent"
                  :style="{ paddingLeft: row.depth * 18 + 'px' }"
                >
                  <button
                    v-if="row.hasChildren"
                    class="expand-btn"
                    :title="expandedIds.has(row.record.id) ? 'Collapse' : 'Expand children'"
                    @click.stop="toggleExpand(row.record.id)"
                  >
                    {{ expandedIds.has(row.record.id) ? '▼' : '▶' }}
                  </button>
                  <span v-else-if="row.depth > 0" class="tree-leaf" aria-hidden="true">└</span>
                </span>
                <code class="id-cell">{{ row.record.id }}</code>
              </td>
              <td>{{ row.record.serial ?? '—' }}</td>
              <td>{{ row.record.articleName ?? '—' }}</td>
              <td><code class="part-cell">{{ row.record.partNumber ?? '—' }}</code></td>
              <td>{{ row.record.status ?? '—' }}</td>
              <td>{{ row.record.commissionDate ?? '—' }}</td>
              <td>
                <span :class="['badge', planBadgeClass(row.record.maintenancePlanStatus)]">
                  {{ row.record.maintenancePlanStatus ?? 'None' }}
                </span>
                <span v-if="row.record.allocationChartRef" class="chart-ref">
                  {{ row.record.allocationChartRef }}
                </span>
              </td>
              <td>{{ row.record.parentSerial ?? '—' }}</td>
            </tr>

            <!-- Expandable detail panel -->
            <tr v-if="detailId === row.record.id" class="detail-row">
              <td colspan="9">
                <div class="detail-panel">
                  <div class="detail-grid">
                    <div class="detail-item">
                      <span class="dl">Full ID</span>
                      <code>{{ row.record.id }}</code>
                    </div>
                    <div class="detail-item">
                      <span class="dl">Manufacturer</span>
                      <span>{{ row.record.manufacturer ?? '—' }}</span>
                    </div>
                    <div class="detail-item">
                      <span class="dl">Allocation Chart</span>
                      <code>{{ row.record.allocationChartRef ?? '—' }}</code>
                    </div>
                    <div class="detail-item" v-if="row.record.exclusionReason">
                      <span class="dl">Exclusion Reason</span>
                      <span class="excl-reason">{{ row.record.exclusionReason }}</span>
                    </div>
                    <div class="detail-item" v-if="row.record.parentId">
                      <span class="dl">Parent ID</span>
                      <code>{{ row.record.parentId }}</code>
                    </div>
                  </div>
                  <div class="detail-excluded">
                    <span class="excl-heading">Not exported by pipeline:</span>
                    <div class="excl-fields">
                      <div class="excl-field">
                        <code class="excl-key">technician_name</code>
                        <span class="excl-val">{{ row.record.technicianName ?? '—' }}</span>
                        <span class="excl-tag tag-gdpr">GDPR Art. 5(1)(c)</span>
                      </div>
                      <div class="excl-field">
                        <code class="excl-key">storage_location</code>
                        <span class="excl-val">{{ row.record.storageLocation ?? '—' }}</span>
                        <span class="excl-tag tag-pending">Open Point #4</span>
                      </div>
                    </div>
                  </div>
                </div>
              </td>
            </tr>
          </template>
        </tbody>
      </table>

      <p v-if="displayRows.length === 0" class="info no-results">
        No records match the current filter.
      </p>

      <!-- Bottom excluded-fields summary -->
      <div class="excluded-fields-note">
        <h2>Fields present in ERP but not exported</h2>
        <table class="note-table">
          <thead>
            <tr>
              <th>ERP Field</th>
              <th>Example value (first in-scope CI)</th>
              <th>Reason not exported</th>
            </tr>
          </thead>
          <tbody>
            <tr>
              <td><code>technician_name</code></td>
              <td class="example-val">
                {{ records.find((r) => r.inScope)?.technicianName ?? '—' }}
              </td>
              <td class="reason-gdpr">
                GDPR Art. 5(1)(c) — personal data; stripped by DataMinimizer before any file is written
              </td>
            </tr>
            <tr>
              <td><code>storage_location</code></td>
              <td class="example-val">
                {{ records.find((r) => r.inScope)?.storageLocation ?? '—' }}
              </td>
              <td class="reason-pending">
                Open Point #4 — vendor entitlement not yet confirmed; excluded until ICD is updated
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </template>
  </div>
</template>

<style scoped>
.toolbar {
  display: flex;
  align-items: center;
  gap: 1rem;
  margin-bottom: 1rem;
}

h1 {
  margin: 0;
  font-size: 1.25rem;
  flex-shrink: 0;
}

.search-box {
  flex: 1;
  max-width: 300px;
  padding: 0.3rem 0.6rem;
  border: 1px solid #cbd5e1;
  border-radius: 0.375rem;
  font-size: 0.85rem;
  color: #1e293b;
  background: #fff;
}

.search-box:focus {
  outline: 2px solid #4f46e5;
  outline-offset: 1px;
}

.summary-bar {
  display: flex;
  align-items: center;
  gap: 0.6rem;
  margin-bottom: 0.75rem;
}

.pill {
  display: inline-block;
  padding: 0.2rem 0.6rem;
  border-radius: 9999px;
  font-size: 0.78rem;
  font-weight: 600;
}

.pill-green { background: #dcfce7; color: #166534; }
.pill-red   { background: #fee2e2; color: #991b1b; }

.total { font-size: 0.85rem; color: #64748b; }

.mode-tag {
  padding: 0.15rem 0.5rem;
  border-radius: 0.25rem;
  font-size: 0.72rem;
  font-weight: 600;
  background: #ede9fe;
  color: #4338ca;
}

.mode-flat {
  background: #f1f5f9;
  color: #475569;
}

.filter-row {
  display: flex;
  gap: 0.5rem;
  margin-bottom: 0.75rem;
}

.filter-btn {
  padding: 0.25rem 0.75rem;
  border: 1px solid #cbd5e1;
  border-radius: 0.375rem;
  background: #fff;
  font-size: 0.8rem;
  cursor: pointer;
  color: #475569;
}

.filter-btn.active {
  background: #1a1a2e;
  color: #e2e8f0;
  border-color: #1a1a2e;
}

table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.85rem;
}

th, td {
  padding: 0.45rem 0.65rem;
  text-align: left;
  border-bottom: 1px solid #e2e8f0;
  vertical-align: middle;
}

th {
  background: #f8fafc;
  font-weight: 600;
  font-size: 0.75rem;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  white-space: nowrap;
}

/* Sortable column header button */
.sort-btn {
  background: none;
  border: none;
  padding: 0;
  font: inherit;
  font-size: 0.75rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: inherit;
  cursor: pointer;
  display: flex;
  align-items: center;
  gap: 0.25rem;
  white-space: nowrap;
}

.sort-btn:hover { color: #4f46e5; }

.sort-icon { color: #94a3b8; font-style: normal; }
.sort-icon.active { color: #4f46e5; }

/* Data rows */
.data-row { cursor: pointer; }
.data-row:hover td { filter: brightness(0.97); }

.row-in-scope td { background: #f0fdf4; }
.row-excluded td  { background: #fef2f2; color: #9ca3af; }
.row-selected td  { outline: 2px solid #4f46e5; outline-offset: -1px; }

/* Tree */
.id-col { white-space: nowrap; }

.tree-indent {
  display: inline-flex;
  align-items: center;
  gap: 0.2rem;
}

.expand-btn {
  background: none;
  border: 1px solid #cbd5e1;
  border-radius: 0.25rem;
  width: 1.3rem;
  height: 1.3rem;
  font-size: 0.6rem;
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  color: #475569;
  flex-shrink: 0;
}

.expand-btn:hover {
  background: #e2e8f0;
}

.tree-leaf {
  display: inline-block;
  width: 1.3rem;
  text-align: center;
  color: #94a3b8;
  font-size: 0.85rem;
}

.id-cell {
  font-size: 0.78rem;
  color: #475569;
}

.part-cell {
  font-size: 0.82rem;
}

/* Badges */
.badge {
  display: inline-block;
  padding: 0.15rem 0.45rem;
  border-radius: 9999px;
  font-size: 0.72rem;
  font-weight: 600;
  white-space: nowrap;
}

.badge-inscope       { background: #dcfce7; color: #166534; }
.badge-excluded      { background: #fee2e2; color: #991b1b; }
.badge-plan-active   { background: #dcfce7; color: #166534; }
.badge-plan-inactive { background: #fef9c3; color: #854d0e; }
.badge-plan-none     { background: #f1f5f9; color: #64748b; }

.chart-ref {
  margin-left: 0.4rem;
  font-size: 0.72rem;
  color: #94a3b8;
}

/* Detail panel */
.detail-row td {
  background: #f8fafc !important;
  padding: 0;
  border-bottom: 2px solid #4f46e5;
}

.detail-panel {
  padding: 0.75rem 1rem;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.detail-grid {
  display: flex;
  flex-wrap: wrap;
  gap: 1rem 2rem;
}

.detail-item {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
  min-width: 160px;
}

.dl {
  font-size: 0.7rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: #64748b;
}

.excl-reason { color: #991b1b; font-size: 0.85rem; }

.detail-excluded {
  padding-top: 0.5rem;
  border-top: 1px solid #e2e8f0;
}

.excl-heading {
  font-size: 0.72rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: #94a3b8;
  display: block;
  margin-bottom: 0.4rem;
}

.excl-fields {
  display: flex;
  flex-direction: column;
  gap: 0.3rem;
}

.excl-field {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.82rem;
}

.excl-key { color: #475569; }

.excl-val {
  color: #64748b;
  font-style: italic;
}

.excl-tag {
  padding: 0.1rem 0.4rem;
  border-radius: 0.25rem;
  font-size: 0.7rem;
  font-weight: 600;
}

.tag-gdpr    { background: #fee2e2; color: #991b1b; }
.tag-pending { background: #fef9c3; color: #854d0e; }

/* Bottom note table */
.no-results { color: #64748b; margin-top: 1rem; }

.excluded-fields-note {
  margin-top: 2rem;
  padding-top: 1.5rem;
  border-top: 1px solid #e2e8f0;
}

.excluded-fields-note h2 {
  font-size: 1rem;
  font-weight: 600;
  margin: 0 0 0.75rem;
  color: #1e293b;
}

.note-table { max-width: 860px; font-size: 0.85rem; }

.note-table th {
  background: #f8fafc;
  font-weight: 600;
  font-size: 0.75rem;
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.example-val { font-style: italic; color: #64748b; }
.reason-gdpr    { color: #b45309; }
.reason-pending { color: #4338ca; }

.info  { color: #64748b; }
.error { color: #dc2626; }
</style>
