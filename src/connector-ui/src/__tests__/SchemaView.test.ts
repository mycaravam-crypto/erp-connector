import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import SchemaView from '@/views/SchemaView.vue'
import * as connectionApi from '@/api/connection'
import * as erpApi from '@/api/erp'
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
        { name: 'id',           type: 'uuid',                      nullable: false, primaryKey: true },
        { name: 'serial',       type: 'character varying(100)',     nullable: true,  primaryKey: false },
        { name: 'article_id',   type: 'uuid',                      nullable: true,  primaryKey: false },
        { name: 'status',       type: 'character varying(50)',      nullable: true,  primaryKey: false },
      ],
    },
    {
      name: 'masterdata',
      description: 'Article master',
      columns: [
        { name: 'id',           type: 'uuid',                      nullable: false, primaryKey: true },
        { name: 'article_name', type: 'character varying(200)',     nullable: true,  primaryKey: false },
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
})

describe('SchemaView', () => {
  it('shows loading initially', () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockReturnValue(new Promise(() => {}))
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    expect(w.text()).toContain('Loading')
  })

  it('shows error when source schema fetch throws', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockRejectedValueOnce(new Error('network'))
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('Could not reach')
  })

  it('shows error when getSourceSchema returns null', async () => {
    vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValueOnce(null)
    const w = mount(SchemaView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('Configure a database connection')
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
})
