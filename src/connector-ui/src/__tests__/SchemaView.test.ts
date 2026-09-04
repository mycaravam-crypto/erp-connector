import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import SchemaView from '@/views/SchemaView.vue'
import * as connectionApi from '@/api/connection'
import * as erpApi from '@/api/mapping'
import * as pipelineApi from '@/api/pipeline'
import type { SourceSchema } from '@/api/connection'

function buildRouter() {
  const r = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/export-schema', name: 'export-schema', component: SchemaView },
      { path: '/source-schema', name: 'source-schema', component: { template: '<div/>' } },
      { path: '/exports', name: 'exports', component: { template: '<div/>' } },
    ],
  })
  r.push('/export-schema')
  return r
}

const SCHEMA: SourceSchema = {
  connectionLabel: 'localhost:5432/testdb',
  tables: [
    {
      name: 'systemconfiguration',
      description: 'CI instances',
      columns: [
        { name: 'id',           type: 'uuid',                      nullable: false, primaryKey: true,  foreignKeyTable: null,         foreignKeyColumn: null },
        { name: 'serial',       type: 'character varying(100)',     nullable: true,  primaryKey: false, foreignKeyTable: null,         foreignKeyColumn: null },
        { name: 'article_id',   type: 'uuid',                      nullable: true,  primaryKey: false, foreignKeyTable: 'masterdata', foreignKeyColumn: 'id' },
        { name: 'status',       type: 'character varying(50)',      nullable: true,  primaryKey: false, foreignKeyTable: null,         foreignKeyColumn: null },
      ],
    },
    {
      name: 'masterdata',
      description: 'Article master',
      columns: [
        { name: 'id',           type: 'uuid',                      nullable: false, primaryKey: true,  foreignKeyTable: null, foreignKeyColumn: null },
        { name: 'article_name', type: 'character varying(200)',     nullable: true,  primaryKey: false, foreignKeyTable: null, foreignKeyColumn: null },
      ],
    },
    {
      name: 'maintenance_plan',
      description: 'Maintenance plans',
      columns: [
        { name: 'id',                   type: 'uuid',                   nullable: false, primaryKey: true,  foreignKeyTable: null, foreignKeyColumn: null },
        { name: 'status',               type: 'character varying(50)',  nullable: false, primaryKey: false, foreignKeyTable: null, foreignKeyColumn: null },
        { name: 'allocation_chart_ref', type: 'character varying(100)', nullable: true,  primaryKey: false, foreignKeyTable: null, foreignKeyColumn: null },
      ],
    },
  ],
}

beforeEach(() => {
  vi.restoreAllMocks()
  vi.spyOn(erpApi, 'getExportMapping').mockResolvedValue(null)
  vi.spyOn(erpApi, 'saveExportMapping').mockResolvedValue({ ok: true })
  vi.spyOn(erpApi, 'getPresets').mockResolvedValue({})
  vi.spyOn(erpApi, 'savePreset').mockResolvedValue({ ok: true })
  vi.spyOn(erpApi, 'deletePreset').mockResolvedValue({ ok: true })
  vi.spyOn(pipelineApi, 'getPreview').mockResolvedValue({
    recordCount: 0,
    schemaVersion: '1',
    columns: [],
    records: [],
    source: 'dynamic',
  })
})

describe('SchemaView', () => {
  it('shows loading initially', () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockReturnValue(new Promise(() => {}))
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    expect(w.text()).toContain('Loading')
  })

  it('shows the rejection message when source schema fetch throws', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockRejectedValueOnce(new Error('network'))
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('network')
  })

  it('shows the server error detail when introspection fails', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockRejectedValueOnce(
      new Error('Could not read the schema from prod-db:5432/erp: relation "masterdata" does not exist'),
    )
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('relation "masterdata" does not exist')
  })

  it('shows table selector with available tables', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('systemconfiguration')
    expect(w.text()).toContain('masterdata')
  })

  it('shows connection label chip', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.find('.conn-chip').text()).toContain('localhost:5432/testdb')
  })

  it('shows column rows after a table is selected', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    const sel = w.find('select.table-select')
    await sel.setValue('systemconfiguration')
    await w.vm.$nextTick()

    const tbody = w.find('.col-table tbody')
    expect(tbody.findAll('tr')).toHaveLength(4)
    expect(tbody.text()).toContain('id')
    expect(tbody.text()).toContain('serial')
  })

  it('marks the PK column with PK badge', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.table-select').setValue('systemconfiguration')
    await w.vm.$nextTick()

    expect(w.find('.pk-badge').text()).toBe('PK')
  })

  it('shows Add Relation button after table is selected', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.table-select').setValue('systemconfiguration')
    await w.vm.$nextTick()

    expect(w.find('.add-btn').exists()).toBe(true)
  })

  it('adds a relation row when Add Relation is clicked', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.table-select').setValue('systemconfiguration')
    await w.vm.$nextTick()

    expect(w.findAll('.relation-card')).toHaveLength(0)
    await w.find('.add-btn').trigger('click')
    expect(w.findAll('.relation-card')).toHaveLength(1)
  })

  it('removes a relation row when × is clicked', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.table-select').setValue('systemconfiguration')
    await w.vm.$nextTick()

    await w.find('.add-btn').trigger('click')
    expect(w.findAll('.relation-card')).toHaveLength(1)
    await w.find('.rel-remove-btn').trigger('click')
    expect(w.findAll('.relation-card')).toHaveLength(0)
  })

  it('shows a suggested relation card for an inbound foreign key', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.table-select').setValue('masterdata')
    await w.vm.$nextTick()

    const cards = w.findAll('.suggested-relation-card')
    expect(cards).toHaveLength(1)
    expect(cards[0].text()).toContain('systemconfiguration.article_id')
    expect(cards[0].text()).toContain('masterdata.id')
  })

  it('does not suggest relations for a table with no inbound foreign keys', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.table-select').setValue('systemconfiguration')
    await w.vm.$nextTick()

    expect(w.findAll('.suggested-relation-card')).toHaveLength(0)
  })

  it('adds a prefilled relation and removes the suggestion when Add is clicked', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.table-select').setValue('masterdata')
    await w.vm.$nextTick()

    expect(w.findAll('.relation-card')).toHaveLength(0)
    await w.find('.suggested-add-btn').trigger('click')

    const cards = w.findAll('.relation-card')
    expect(cards).toHaveLength(1)
    expect(cards[0].find('select').element.value).toBe('systemconfiguration')
    expect(w.findAll('.suggested-relation-card')).toHaveLength(0)
  })

  it('selecting a related table populates an unchecked field picker for every column', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.table-select').setValue('systemconfiguration')
    await w.vm.$nextTick()
    await w.find('.add-btn').trigger('click')

    const card = w.find('.relation-card')
    await card.find('select').setValue('masterdata')
    await w.vm.$nextTick()

    const rows = card.findAll('.field-picker-table tbody tr')
    expect(rows).toHaveLength(2) // masterdata: id, article_name
    rows.forEach((row) => {
      expect((row.find('input[type="checkbox"]').element as HTMLInputElement).checked).toBe(false)
    })
  })

  it('Select All / Deselect All toggle every field within a relation card', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.table-select').setValue('systemconfiguration')
    await w.vm.$nextTick()
    await w.find('.add-btn').trigger('click')

    const card = w.find('.relation-card')
    await card.find('select').setValue('masterdata')
    await w.vm.$nextTick()

    await card.find('.field-picker-select-all-btn').trigger('click')
    let checkboxes = card.findAll('.field-picker-table input[type="checkbox"]')
    expect(checkboxes.every((cb) => (cb.element as HTMLInputElement).checked)).toBe(true)

    await card.find('.field-picker-deselect-all-btn').trigger('click')
    checkboxes = card.findAll('.field-picker-table input[type="checkbox"]')
    expect(checkboxes.every((cb) => !(cb.element as HTMLInputElement).checked)).toBe(true)
  })

  it('renaming a relation field export-as value persists', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.table-select').setValue('systemconfiguration')
    await w.vm.$nextTick()
    await w.find('.add-btn').trigger('click')

    const card = w.find('.relation-card')
    await card.find('select').setValue('masterdata')
    await w.vm.$nextTick()

    const input = card.find('.field-picker-target-input')
    await input.setValue('article_display_name')
    expect((input.element as HTMLInputElement).value).toBe('article_display_name')
  })

  it('switching a relation card related table resets its field list', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.table-select').setValue('systemconfiguration')
    await w.vm.$nextTick()
    await w.find('.add-btn').trigger('click')

    const card = w.find('.relation-card')
    await card.find('select').setValue('masterdata')
    await w.vm.$nextTick()
    expect(card.findAll('.field-picker-table tbody tr')).toHaveLength(2) // masterdata columns

    await card.find('select').setValue('maintenance_plan')
    await w.vm.$nextTick()
    expect(card.findAll('.field-picker-table tbody tr')).toHaveLength(3) // maintenance_plan columns
  })

  it('shows format buttons', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.table-select').setValue('systemconfiguration')
    await w.vm.$nextTick()

    const fmts = w.findAll('.format-btn')
    expect(fmts.length).toBeGreaterThanOrEqual(3)
    const labels = fmts.map((b) => b.text())
    expect(labels.some((l) => l.includes('xlsx'))).toBe(true)
    expect(labels.some((l) => l.includes('csv'))).toBe(true)
    expect(labels.some((l) => l.includes('json'))).toBe(true)
  })

  it('calls saveExportMapping when Save Mapping is clicked', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const saveSpy = vi.spyOn(erpApi, 'saveExportMapping').mockResolvedValue({ ok: true })
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.table-select').setValue('systemconfiguration')
    await w.vm.$nextTick()

    await w.find('.btn-save').trigger('click')
    await flushPromises()

    expect(saveSpy).toHaveBeenCalledOnce()
    const [config] = saveSpy.mock.calls[0]
    expect(config.sourceTable).toBe('systemconfiguration')
  })

  it('shows save-ok confirmation after successful save', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    vi.spyOn(erpApi, 'saveExportMapping').mockResolvedValue({ ok: true })
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.table-select').setValue('systemconfiguration')
    await w.vm.$nextTick()

    await w.find('.btn-save').trigger('click')
    await flushPromises()

    expect(w.find('.save-ok').exists()).toBe(true)
  })

  it('refreshes the live preview after a successful save', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    vi.spyOn(erpApi, 'saveExportMapping').mockResolvedValue({ ok: true })
    const previewSpy = vi.spyOn(pipelineApi, 'getPreview').mockResolvedValue({
      recordCount: 0,
      schemaVersion: '1',
      columns: [],
      records: [],
      source: 'dynamic',
    })
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    previewSpy.mockClear()

    await w.find('select.table-select').setValue('systemconfiguration')
    await w.vm.$nextTick()
    await w.find('.btn-save').trigger('click')
    await flushPromises()

    expect(previewSpy).toHaveBeenCalledOnce()
  })

  it('warns that Related Table Joins are dropped once a nested group is configured for JSON', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.table-select').setValue('systemconfiguration')
    await w.vm.$nextTick()

    // A relation alone (no nested groups/wrapper) is unaffected — no warning yet.
    await w.find('.add-btn').trigger('click')
    await w.find('.relation-card select').setValue('masterdata')
    await w.vm.$nextTick()
    expect(w.text()).not.toContain('Related Table Joins are ignored')

    // Adding a nested group pushes this mapping onto the nested-JSON path, which silently
    // drops Relations server-side — the warning should now appear.
    await w.find('.add-nested-group-btn').trigger('click')
    await w.vm.$nextTick()

    expect(w.text()).toContain('Related Table Joins are ignored')
  })

  it('shows save-error when backend rejects the mapping', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    vi.spyOn(erpApi, 'saveExportMapping').mockResolvedValue({ ok: false, error: 'Bad request' })
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.table-select').setValue('systemconfiguration')
    await w.vm.$nextTick()

    await w.find('.btn-save').trigger('click')
    await flushPromises()

    expect(w.find('.save-error').exists()).toBe(true)
    expect(w.find('.save-error').text()).toContain('Bad request')
  })

  it('preserves flagged columns when switching tables and switching back', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    // Select table A and enable the 'serial' column
    await w.find('select.table-select').setValue('systemconfiguration')
    await flushPromises()
    const serialCheckbox = w.findAll('input[type="checkbox"]').find(
      (cb) => cb.element.closest('tr')?.textContent?.includes('serial'),
    )
    expect(serialCheckbox).toBeTruthy()
    await serialCheckbox!.setValue(true)

    // Switch to table B
    await w.find('select.table-select').setValue('masterdata')
    await flushPromises()

    // Switch back to table A
    await w.find('select.table-select').setValue('systemconfiguration')
    await flushPromises()

    // 'serial' checkbox state should be preserved
    const restoredCheckbox = w.findAll('input[type="checkbox"]').find(
      (cb) => cb.element.closest('tr')?.textContent?.includes('serial'),
    )
    expect((restoredCheckbox!.element as HTMLInputElement).checked).toBe(true)
  })

  it('restores existing mapping on load', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    vi.spyOn(erpApi, 'getExportMapping').mockResolvedValueOnce({
      sourceTable: 'systemconfiguration',
      fields: [
        { sourceName: 'id', targetName: 'u_cmdb_guid', enabled: true },
        { sourceName: 'serial', targetName: 'serial_number', enabled: true },
        { sourceName: 'article_id', targetName: 'article_id', enabled: false },
        { sourceName: 'status', targetName: 'status', enabled: false },
      ],
      relations: [],
    })
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    expect(w.find('.col-table').exists()).toBe(true)
    const inputs = w.findAll('input.export-as-input')
    const values = inputs.map((i) => (i.element as HTMLInputElement).value)
    expect(values).toContain('u_cmdb_guid')
  })

  it('does not mark the page dirty just from restoring an already-saved mapping on load', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    vi.spyOn(erpApi, 'getExportMapping').mockResolvedValueOnce({
      sourceTable: 'systemconfiguration',
      fields: [{ sourceName: 'id', targetName: 'id', enabled: true }],
      relations: [],
    })
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    // Restoring the saved mapping populates the form but isn't itself an edit — the stale-preview
    // hint (only shown while dirty) must not appear on a page nobody has touched yet.
    expect(w.text()).not.toContain('Showing the last saved mapping')
  })

  // ── Preset tests ───────────────────────────────────────────────────────────

  it('renders preset names from the API in the dropdown', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    vi.spyOn(erpApi, 'getPresets').mockResolvedValue({
      'Parts Export': {
        sourceTable: 'systemconfiguration',
        fields: [{ sourceName: 'id', targetName: 'id', enabled: true }],
        relations: [],
      },
    })
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    const opts = w.find('select.preset-select').findAll('option')
    expect(opts.some((o) => o.text() === 'Parts Export')).toBe(true)
  })

  it('applies preset config when Load is clicked', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    vi.spyOn(erpApi, 'getPresets').mockResolvedValue({
      'My Config': {
        sourceTable: 'systemconfiguration',
        fields: [
          { sourceName: 'id',     targetName: 'guid',   enabled: true },
          { sourceName: 'serial', targetName: 'serial', enabled: true },
          { sourceName: 'article_id', targetName: 'article_id', enabled: false },
          { sourceName: 'status', targetName: 'status', enabled: false },
        ],
        relations: [],
      },
    })
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.preset-select').setValue('My Config')
    await w.find('.load-btn').trigger('click')
    await flushPromises()

    expect((w.find('select.table-select').element as HTMLSelectElement).value).toBe('systemconfiguration')
    const inputs = w.findAll('input.export-as-input')
    const values = inputs.map((i) => (i.element as HTMLInputElement).value)
    expect(values).toContain('guid')
  })

  it('calls savePreset with the current config via inline save-as flow', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const saveSpy = vi.spyOn(erpApi, 'savePreset').mockResolvedValue({ ok: true })

    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.table-select').setValue('systemconfiguration')
    await w.vm.$nextTick()

    // click Save As → inline input appears
    await w.find('.save-as-btn').trigger('click')
    await w.vm.$nextTick()

    const nameInput = w.find('input[placeholder="Preset name…"]')
    expect(nameInput.exists()).toBe(true)
    await nameInput.setValue('New Preset')

    const saveBtn = w.findAll('button').find((b) => b.text() === 'Save')!
    await saveBtn.trigger('click')
    await flushPromises()

    expect(saveSpy).toHaveBeenCalledOnce()
    const [name, config] = saveSpy.mock.calls[0]
    expect(name).toBe('New Preset')
    expect(config.sourceTable).toBe('systemconfiguration')
  })

  it('calls deletePreset and clears selection via inline delete confirm', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    vi.spyOn(erpApi, 'getPresets').mockResolvedValue({
      'Old Preset': {
        sourceTable: 'systemconfiguration',
        fields: [],
        relations: [],
      },
    })
    const deleteSpy = vi.spyOn(erpApi, 'deletePreset').mockResolvedValue({ ok: true })

    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.preset-select').setValue('Old Preset')
    // click Delete → inline confirm row appears
    await w.find('.delete-preset-btn').trigger('click')
    await w.vm.$nextTick()

    const yesBtn = w.findAll('button').find((b) => b.text().includes('Yes, delete'))!
    expect(yesBtn.exists()).toBe(true)
    await yesBtn.trigger('click')
    await flushPromises()

    expect(deleteSpy).toHaveBeenCalledWith('Old Preset')
    expect((w.find('select.preset-select').element as HTMLSelectElement).value).toBe('')
  })

  it('shows inline save input after clicking Save As', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.table-select').setValue('systemconfiguration')
    await w.vm.$nextTick()

    expect(w.find('input[placeholder="Preset name…"]').exists()).toBe(false)
    await w.find('.save-as-btn').trigger('click')
    await w.vm.$nextTick()
    expect(w.find('input[placeholder="Preset name…"]').exists()).toBe(true)
  })

  it('Save button is disabled and inline input shows when name is empty', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.table-select').setValue('systemconfiguration')
    await w.vm.$nextTick()
    await w.find('.save-as-btn').trigger('click')
    await w.vm.$nextTick()

    const saveBtn = w.findAll('button').find((b) => b.text() === 'Save')!
    expect(saveBtn.attributes('disabled')).toBeDefined()

    // pressing Enter with an empty name triggers the error
    await w.find('input[placeholder="Preset name…"]').trigger('keyup', { key: 'Enter' })
    await w.vm.$nextTick()

    expect(w.find('.preset-error').exists()).toBe(true)
    expect(w.find('.preset-error').text()).toContain('cannot be empty')
  })

  it('shows inline delete confirm row after clicking Delete', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    vi.spyOn(erpApi, 'getPresets').mockResolvedValue({
      'My Preset': { sourceTable: 'systemconfiguration', fields: [], relations: [] },
    })
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.preset-select').setValue('My Preset')
    await w.find('.delete-preset-btn').trigger('click')
    await w.vm.$nextTick()

    expect(w.text()).toContain('Yes, delete')
    expect(w.text()).toContain('My Preset')
  })

  it('Select All enables every column', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.table-select').setValue('systemconfiguration')
    await w.vm.$nextTick()

    // Only the PK (id) starts enabled; disable it to start with some unchecked
    const checkboxes = w.findAll('input[type="checkbox"]')
      .filter((cb) => cb.element.closest('tr')) // column checkboxes only
    // Click "Deselect All" first to ensure some are unchecked
    const deselectBtn = w.findAll('button').find((b) => b.text() === 'Deselect All')!
    await deselectBtn.trigger('click')
    await w.vm.$nextTick()

    const allUnchecked = w.findAll('input[type="checkbox"]')
      .filter((cb) => cb.element.closest('tr'))
      .every((cb) => !(cb.element as HTMLInputElement).checked)
    expect(allUnchecked).toBe(true)

    const selectBtn = w.findAll('button').find((b) => b.text() === 'Select All')!
    await selectBtn.trigger('click')
    await w.vm.$nextTick()

    const allChecked = w.findAll('input[type="checkbox"]')
      .filter((cb) => cb.element.closest('tr'))
      .every((cb) => (cb.element as HTMLInputElement).checked)
    expect(allChecked).toBe(true)
  })

  it('Deselect All disables every column', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.table-select').setValue('systemconfiguration')
    await w.vm.$nextTick()

    // Select All first so we have something to deselect
    const selectBtn = w.findAll('button').find((b) => b.text() === 'Select All')!
    await selectBtn.trigger('click')
    await w.vm.$nextTick()

    const deselectBtn = w.findAll('button').find((b) => b.text() === 'Deselect All')!
    await deselectBtn.trigger('click')
    await w.vm.$nextTick()

    const noneChecked = w.findAll('input[type="checkbox"]')
      .filter((cb) => cb.element.closest('tr'))
      .every((cb) => !(cb.element as HTMLInputElement).checked)
    expect(noneChecked).toBe(true)
  })

  it('Save & Go to Export button label is correct', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.table-select').setValue('systemconfiguration')
    await w.vm.$nextTick()

    const proceedBtn = w.findAll('button').find((b) => b.text().includes('Save & Go to Export'))
    expect(proceedBtn).toBeTruthy()
  })

  it('shows preset save error when API returns error', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    vi.spyOn(erpApi, 'savePreset').mockResolvedValue({ ok: false, error: 'Name too long.' })

    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.table-select').setValue('systemconfiguration')
    await w.vm.$nextTick()
    await w.find('.save-as-btn').trigger('click')
    await w.vm.$nextTick()

    await w.find('input[placeholder="Preset name…"]').setValue('x')
    const saveBtn = w.findAll('button').find((b) => b.text() === 'Save')!
    await saveBtn.trigger('click')
    await flushPromises()

    expect(w.find('.preset-error').text()).toContain('Name too long.')
  })

  // ── Step 3 reframe (Nested JSON primary, Related Table Joins advanced) ──────

  it('shows Nested JSON Structure before Related Table Joins on a fresh mapping', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.table-select').setValue('systemconfiguration')
    await w.vm.$nextTick()

    const html = w.html()
    expect(html.indexOf('Nested JSON Structure')).toBeGreaterThan(-1)
    expect(html.indexOf('Nested JSON Structure')).toBeLessThan(html.indexOf('Related Table Joins'))
  })

  it('Related Table Joins is collapsed by default when no relations are configured', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.table-select').setValue('systemconfiguration')
    await w.vm.$nextTick()

    expect(w.find('.legacy-relations-details').attributes('open')).toBeUndefined()
  })

  it('Related Table Joins opens by default when an existing mapping already has relations', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    vi.spyOn(erpApi, 'getExportMapping').mockResolvedValueOnce({
      sourceTable: 'systemconfiguration',
      fields: [{ sourceName: 'id', targetName: 'id', enabled: true }],
      relations: [
        {
          relatedTable: 'masterdata',
          joinKey: 'id',
          sourceJoinKey: 'article_id',
          enabled: true,
          flattenStrategy: 'string_join',
          delimiter: ', ',
          fields: [{ sourceField: 'article_name', targetField: 'article_name', enabled: true }],
        },
      ],
    })
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    expect(w.find('.legacy-relations-details').attributes('open')).toBeDefined()
  })

  it('keeps Nested JSON Structure and its configured groups visible when the preview-format picker is switched', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.table-select').setValue('systemconfiguration')
    await w.vm.$nextTick()
    await w.find('.add-nested-group-btn').trigger('click')
    expect(w.findAll('.nested-group-card')).toHaveLength(1)

    const csvBtn = w.findAll('.format-btn').find((b) => b.text().includes('csv'))!
    await csvBtn.trigger('click')
    await w.vm.$nextTick()

    expect(w.findAll('.nested-group-card')).toHaveLength(1)
  })

  it('does not frame Nested JSON Structure as optional', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.table-select').setValue('systemconfiguration')
    await w.vm.$nextTick()

    expect(w.text()).not.toContain('JSON Export Options')
    expect(w.text()).toContain('advanced — flat/legacy exports')
  })

  // ── Convert to Nested Group ──────────────────────────────────────────────────

  it('converts a relation to an equivalent nested group and keeps the relation when declined', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    vi.spyOn(window, 'confirm').mockReturnValue(false)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.table-select').setValue('systemconfiguration')
    await w.vm.$nextTick()
    await w.find('.add-btn').trigger('click')
    await w.find('.relation-card select').setValue('masterdata')
    await w.vm.$nextTick()

    expect(w.findAll('.nested-group-card')).toHaveLength(0)
    await w.find('.convert-to-nested-group-btn').trigger('click')

    expect(w.findAll('.nested-group-card')).toHaveLength(1)
    expect(w.findAll('.relation-card')).toHaveLength(1)
    expect(w.find('.nested-group-card').text()).toContain('masterdata')
  })

  it('removes the source relation when the user confirms after converting', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.table-select').setValue('systemconfiguration')
    await w.vm.$nextTick()
    await w.find('.add-btn').trigger('click')
    await w.find('.relation-card select').setValue('masterdata')
    await w.vm.$nextTick()

    await w.find('.convert-to-nested-group-btn').trigger('click')

    expect(w.findAll('.nested-group-card')).toHaveLength(1)
    expect(w.findAll('.relation-card')).toHaveLength(0)
  })

  // ── Nested JSON editor polish: shape preview + inline validation ────────────

  it('shows a JSON shape preview once a nested group has fields enabled', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.table-select').setValue('systemconfiguration')
    await w.vm.$nextTick()
    await w.find('.add-nested-group-btn').trigger('click')

    const group = w.find('.nested-group-card')
    await group.find('.related-table-select').setValue('masterdata')
    await w.vm.$nextTick()
    await group.find('.field-picker-select-all-btn').trigger('click')
    await w.vm.$nextTick()

    const preview = w.find('.shape-preview pre')
    expect(preview.exists()).toBe(true)
    expect(preview.text()).toContain('article_name')
  })

  it('flags a missing join column inline on a nested group once it has a related table', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.table-select').setValue('systemconfiguration')
    await w.vm.$nextTick()
    await w.find('.add-nested-group-btn').trigger('click')

    const group = w.find('.nested-group-card')
    await group.find('.related-table-select').setValue('masterdata')
    await w.vm.$nextTick()

    expect(group.find('.validation-alert').exists()).toBe(true)
    expect(group.find('.validation-alert').text()).toContain('Join column is required.')
  })

  it('flags a duplicate export key between sibling nested groups', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(SCHEMA)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('select.table-select').setValue('systemconfiguration')
    await w.vm.$nextTick()
    await w.find('.add-nested-group-btn').trigger('click')
    await w.find('.add-nested-group-btn').trigger('click')

    const groups = w.findAll('.nested-group-card')
    await groups[0].find('.export-key-input').setValue('dup')
    await groups[1].find('.export-key-input').setValue('dup')
    await w.vm.$nextTick()

    expect(groups[1].find('.validation-alert').text()).toContain('already used by another nested group')
  })
})
