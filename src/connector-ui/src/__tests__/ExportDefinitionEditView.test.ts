import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import ExportDefinitionEditView from '@/views/ExportDefinitionEditView.vue'
import * as exportDefinitionsApi from '@/api/exportDefinitions'
import * as connectionApi from '@/api/connection'
import type { ExportDefinition } from '@/api/exportDefinitions'
import type { SourceSchema } from '@/api/connection'

// Awaits the initial navigation before returning: reading route.params synchronously at setup
// time (as ExportDefinitionEditView's isNew/id do) would otherwise race the pending push from an
// unresolved "" location, since createRouter's first navigation is asynchronous.
async function buildRouter(id: number | string = 1) {
  const r = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/export-definitions', name: 'export-definitions', component: { template: '<div/>' } },
      { path: '/export-definitions/:id', name: 'export-definition-edit', component: ExportDefinitionEditView },
    ],
  })
  await r.push(`/export-definitions/${id}`)
  return r
}

const SCHEMA: SourceSchema = {
  connectionLabel: 'test',
  tables: [
    {
      name: 'masterdata',
      description: '',
      columns: [
        {
          name: 'part_number',
          type: 'text',
          nullable: false,
          primaryKey: false,
          foreignKeyTable: null,
          foreignKeyColumn: null,
        },
        {
          name: 'serial_number',
          type: 'text',
          nullable: false,
          primaryKey: false,
          foreignKeyTable: null,
          foreignKeyColumn: null,
        },
      ],
    },
  ],
}

const DEFINITION: ExportDefinition = {
  id: 1,
  name: 'Legacy Export',
  description: null,
  rootTable: 'masterdata',
  outputFormat: 'json',
  isEnabled: false,
  schedule: null,
  configVersion: 1,
  createdBy: 'migration',
  createdAt: '2026-08-01T00:00:00Z',
  updatedBy: null,
  updatedAt: null,
  rootNode: {
    targetKey: 'root',
    kind: 'root',
    sourceField: null,
    relatedTable: null,
    joinKey: null,
    sourceJoinKey: null,
    filter: null,
    mapping: null,
    enabled: true,
    children: [
      {
        targetKey: 'part_number',
        kind: 'scalar-field',
        sourceField: 'part_number',
        relatedTable: null,
        joinKey: null,
        sourceJoinKey: null,
        filter: null,
        mapping: { defaultValue: null, transform: 'none', transformArg: null, dataType: 'string' },
        enabled: true,
        children: [],
      },
    ],
  },
}

beforeEach(() => {
  vi.restoreAllMocks()
  vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValue(SCHEMA)
  vi.spyOn(exportDefinitionsApi, 'listExportDefinitionRuns').mockResolvedValue([])
})

describe('ExportDefinitionEditView', () => {
  it('shows loading state initially', async () => {
    vi.spyOn(exportDefinitionsApi, 'getExportDefinition').mockReturnValue(new Promise(() => {}))
    const w = mount(ExportDefinitionEditView, { global: { plugins: [await buildRouter()] } })
    expect(w.text()).toContain('Loading')
  })

  it('shows a not-found message when the definition does not exist', async () => {
    vi.spyOn(exportDefinitionsApi, 'getExportDefinition').mockResolvedValueOnce(null)
    const w = mount(ExportDefinitionEditView, { global: { plugins: [await buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('Export definition not found.')
  })

  it('renders the root table and existing field from the loaded definition', async () => {
    vi.spyOn(exportDefinitionsApi, 'getExportDefinition').mockResolvedValueOnce(DEFINITION)
    const w = mount(ExportDefinitionEditView, { global: { plugins: [await buildRouter()] } })
    await flushPromises()
    expect((w.find('select[aria-label="Root table"]').element as HTMLSelectElement).value).toBe('masterdata')
    expect((w.find('select[aria-label="Source column"]').element as HTMLSelectElement).value).toBe('part_number')
  })

  it('saves the edited name and source field', async () => {
    vi.spyOn(exportDefinitionsApi, 'getExportDefinition').mockResolvedValueOnce(DEFINITION)
    const updateSpy = vi
      .spyOn(exportDefinitionsApi, 'updateExportDefinition')
      .mockResolvedValueOnce({ ok: true, data: { ...DEFINITION, name: 'Renamed Export' } })
    const w = mount(ExportDefinitionEditView, { global: { plugins: [await buildRouter()] } })
    await flushPromises()

    await w.find('input[aria-label="Name"]').setValue('Renamed Export')
    await w.find('select[aria-label="Source column"]').setValue('serial_number')
    const saveBtn = w.findAll('button').find((b) => b.text() === 'Save')!
    await saveBtn.trigger('click')
    await flushPromises()

    expect(updateSpy).toHaveBeenCalledWith(
      1,
      expect.objectContaining({
        name: 'Renamed Export',
        rootNode: expect.objectContaining({
          children: [expect.objectContaining({ sourceField: 'serial_number' })],
        }),
      }),
    )
    expect(w.text()).toContain('Saved.')
  })

  it('shows the server error message when save fails', async () => {
    vi.spyOn(exportDefinitionsApi, 'getExportDefinition').mockResolvedValueOnce(DEFINITION)
    vi.spyOn(exportDefinitionsApi, 'updateExportDefinition').mockResolvedValueOnce({
      ok: false,
      error: 'RootTable is required and must be a valid identifier.',
    })
    const w = mount(ExportDefinitionEditView, { global: { plugins: [await buildRouter()] } })
    await flushPromises()

    const saveBtn = w.findAll('button').find((b) => b.text() === 'Save')!
    await saveBtn.trigger('click')
    await flushPromises()

    expect(w.text()).toContain('RootTable is required and must be a valid identifier.')
  })

  it('runs a test against the live connection and shows the result', async () => {
    vi.spyOn(exportDefinitionsApi, 'getExportDefinition').mockResolvedValueOnce(DEFINITION)
    vi.spyOn(exportDefinitionsApi, 'testExportDefinition').mockResolvedValueOnce({
      ok: true,
      data: {
        runId: 5,
        status: 'Success',
        recordCount: 3,
        configVersion: 1,
        startedAt: '2026-08-27T00:00:00Z',
        finishedAt: '2026-08-27T00:00:01Z',
        errorMessage: null,
        isTestRun: true,
      },
    })
    const w = mount(ExportDefinitionEditView, { global: { plugins: [await buildRouter()] } })
    await flushPromises()

    const testBtn = w.findAll('button').find((b) => b.text().includes('Test against live connection'))!
    await testBtn.trigger('click')
    await flushPromises()

    expect(w.text()).toContain('Test succeeded — 3 record(s) read.')
  })

  it('shows the failure detail when a test run fails', async () => {
    vi.spyOn(exportDefinitionsApi, 'getExportDefinition').mockResolvedValueOnce(DEFINITION)
    vi.spyOn(exportDefinitionsApi, 'testExportDefinition').mockResolvedValueOnce({
      ok: true,
      data: {
        runId: 6,
        status: 'Failed',
        recordCount: 0,
        configVersion: 1,
        startedAt: '2026-08-27T00:00:00Z',
        finishedAt: '2026-08-27T00:00:01Z',
        errorMessage: 'relation "masterdata" does not exist',
        isTestRun: true,
      },
    })
    const w = mount(ExportDefinitionEditView, { global: { plugins: [await buildRouter()] } })
    await flushPromises()

    const testBtn = w.findAll('button').find((b) => b.text().includes('Test against live connection'))!
    await testBtn.trigger('click')
    await flushPromises()

    expect(w.text()).toContain('relation "masterdata" does not exist')
  })

  it('previews the definition and renders the returned records', async () => {
    vi.spyOn(exportDefinitionsApi, 'getExportDefinition').mockResolvedValueOnce(DEFINITION)
    vi.spyOn(exportDefinitionsApi, 'previewExportDefinition').mockResolvedValueOnce({
      ok: true,
      data: { recordCount: 1, records: [{ part_number: 'ABC-1' }] },
    })
    const w = mount(ExportDefinitionEditView, { global: { plugins: [await buildRouter()] } })
    await flushPromises()

    const previewBtn = w.findAll('button').find((b) => b.text() === 'Preview')!
    await previewBtn.trigger('click')
    await flushPromises()

    expect(w.text()).toContain('ABC-1')
    expect(w.text()).toContain('1 record(s) (capped)')
  })

  it('renders the execution history from the backend', async () => {
    vi.spyOn(exportDefinitionsApi, 'getExportDefinition').mockResolvedValueOnce(DEFINITION)
    vi.spyOn(exportDefinitionsApi, 'listExportDefinitionRuns').mockResolvedValueOnce([
      {
        id: 9,
        configVersion: 1,
        startedAt: '2026-08-27T00:00:00Z',
        finishedAt: '2026-08-27T00:00:01Z',
        status: 'Success',
        recordCount: 7,
        errorMessage: null,
        triggeredBy: 'scheduler',
        isTestRun: false,
      },
    ])
    const w = mount(ExportDefinitionEditView, { global: { plugins: [await buildRouter()] } })
    await flushPromises()

    expect(w.text()).toContain('scheduler')
    expect(w.text()).toContain('Success')
  })

  describe('create mode (id = "new")', () => {
    it('does not fetch an existing definition and shows the create form', async () => {
      const getSpy = vi.spyOn(exportDefinitionsApi, 'getExportDefinition')
      const w = mount(ExportDefinitionEditView, { global: { plugins: [await buildRouter('new')] } })
      await flushPromises()

      expect(getSpy).not.toHaveBeenCalled()
      expect(w.text()).toContain('New Export Definition')
      expect(w.findAll('button').some((b) => b.text() === 'Create')).toBe(true)
    })

    it('creates the definition and navigates to its edit page', async () => {
      const createSpy = vi
        .spyOn(exportDefinitionsApi, 'createExportDefinition')
        .mockResolvedValueOnce({ ok: true, data: { ...DEFINITION, id: 42, name: 'New One' } })
      const router = await buildRouter('new')
      const w = mount(ExportDefinitionEditView, { global: { plugins: [router] } })
      await flushPromises()

      await w.find('select[aria-label="Root table"]').setValue('masterdata')
      await w.find('input[aria-label="Name"]').setValue('New One')
      const createBtn = w.findAll('button').find((b) => b.text() === 'Create')!
      await createBtn.trigger('click')
      await flushPromises()

      expect(createSpy).toHaveBeenCalledWith(expect.objectContaining({ name: 'New One', rootTable: 'masterdata' }))
      expect(router.currentRoute.value.params.id).toBe('42')
    })
  })
})
