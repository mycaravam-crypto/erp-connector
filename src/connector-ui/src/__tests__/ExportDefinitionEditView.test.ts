import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import ExportDefinitionEditView from '@/views/ExportDefinitionEditView.vue'
import * as exportDefinitionsApi from '@/api/exportDefinitions'
import type { ExportDefinition } from '@/api/exportDefinitions'

function buildRouter(id = 1) {
  const r = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/export-definitions', name: 'export-definitions', component: { template: '<div/>' } },
      { path: '/export-definitions/:id', name: 'export-definition-edit', component: ExportDefinitionEditView },
    ],
  })
  r.push(`/export-definitions/${id}`)
  return r
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
        mapping: null,
        enabled: true,
        children: [],
      },
    ],
  },
}

beforeEach(() => vi.restoreAllMocks())

describe('ExportDefinitionEditView', () => {
  it('shows loading state initially', () => {
    vi.spyOn(exportDefinitionsApi, 'getExportDefinition').mockReturnValue(new Promise(() => {}))
    const w = mount(ExportDefinitionEditView, { global: { plugins: [buildRouter()] } })
    expect(w.text()).toContain('Loading')
  })

  it('shows a not-found message when the definition does not exist', async () => {
    vi.spyOn(exportDefinitionsApi, 'getExportDefinition').mockResolvedValueOnce(null)
    const w = mount(ExportDefinitionEditView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('Export definition not found.')
  })

  it('renders the root table and field source inputs from the loaded definition', async () => {
    vi.spyOn(exportDefinitionsApi, 'getExportDefinition').mockResolvedValueOnce(DEFINITION)
    const w = mount(ExportDefinitionEditView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect((w.find('input[placeholder="e.g. systemconfiguration"]').element as HTMLInputElement).value).toBe(
      'masterdata',
    )
    expect((w.find('input[placeholder="table.column or column"]').element as HTMLInputElement).value).toBe(
      'part_number',
    )
  })

  it('saves the edited root table and source field', async () => {
    vi.spyOn(exportDefinitionsApi, 'getExportDefinition').mockResolvedValueOnce(DEFINITION)
    const updateSpy = vi
      .spyOn(exportDefinitionsApi, 'updateExportDefinition')
      .mockResolvedValueOnce({ ok: true, data: { ...DEFINITION, rootTable: 'systemconfiguration' } })
    const w = mount(ExportDefinitionEditView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    await w.find('input[placeholder="e.g. systemconfiguration"]').setValue('systemconfiguration')
    const saveBtn = w.findAll('button').find((b) => b.text() === 'Save')!
    await saveBtn.trigger('click')
    await flushPromises()

    expect(updateSpy).toHaveBeenCalledWith(
      1,
      expect.objectContaining({ rootTable: 'systemconfiguration' }),
    )
    expect(w.text()).toContain('Saved.')
  })

  it('shows the server error message when save fails', async () => {
    vi.spyOn(exportDefinitionsApi, 'getExportDefinition').mockResolvedValueOnce(DEFINITION)
    vi.spyOn(exportDefinitionsApi, 'updateExportDefinition').mockResolvedValueOnce({
      ok: false,
      error: 'RootTable is required and must be a valid identifier.',
    })
    const w = mount(ExportDefinitionEditView, { global: { plugins: [buildRouter()] } })
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
    const w = mount(ExportDefinitionEditView, { global: { plugins: [buildRouter()] } })
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
    const w = mount(ExportDefinitionEditView, { global: { plugins: [buildRouter()] } })
    await flushPromises()

    const testBtn = w.findAll('button').find((b) => b.text().includes('Test against live connection'))!
    await testBtn.trigger('click')
    await flushPromises()

    expect(w.text()).toContain('relation "masterdata" does not exist')
  })
})
