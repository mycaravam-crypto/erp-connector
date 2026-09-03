import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import ExportDefinitionsView from '@/views/ExportDefinitionsView.vue'
import * as exportDefinitionsApi from '@/api/exportDefinitions'
import type { ExportDefinitionSummary } from '@/api/exportDefinitions'

async function buildRouter() {
  const r = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/export-definitions', name: 'export-definitions', component: ExportDefinitionsView },
      { path: '/export-definitions/:id', name: 'export-definition-edit', component: { template: '<div/>' } },
    ],
  })
  await r.push('/export-definitions')
  return r
}

const DEFINITIONS: ExportDefinitionSummary[] = [
  {
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
  },
]

beforeEach(() => {
  vi.restoreAllMocks()
  vi.spyOn(exportDefinitionsApi, 'listExportDefinitionRuns').mockResolvedValue([])
})

describe('ExportDefinitionsView', () => {
  it('shows loading state initially', async () => {
    vi.spyOn(exportDefinitionsApi, 'listExportDefinitions').mockReturnValue(new Promise(() => {}))
    const w = mount(ExportDefinitionsView, { global: { plugins: [await buildRouter()] } })
    expect(w.text()).toContain('Loading')
  })

  it('shows an error message when the API rejects', async () => {
    vi.spyOn(exportDefinitionsApi, 'listExportDefinitions').mockRejectedValueOnce(new Error('network'))
    const w = mount(ExportDefinitionsView, { global: { plugins: [await buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('Could not load export definitions')
  })

  it('shows an empty-state message when there are no definitions', async () => {
    vi.spyOn(exportDefinitionsApi, 'listExportDefinitions').mockResolvedValueOnce([])
    const w = mount(ExportDefinitionsView, { global: { plugins: [await buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('No export definitions yet.')
  })

  it('lists definitions with an edit link', async () => {
    vi.spyOn(exportDefinitionsApi, 'listExportDefinitions').mockResolvedValueOnce(DEFINITIONS)
    const w = mount(ExportDefinitionsView, { global: { plugins: [await buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('Legacy Export')
    expect(w.text()).toContain('masterdata')
    expect(w.find('a[href="/export-definitions/1"]').exists()).toBe(true)
  })

  it('has a link to create a new definition', async () => {
    vi.spyOn(exportDefinitionsApi, 'listExportDefinitions').mockResolvedValueOnce([])
    const w = mount(ExportDefinitionsView, { global: { plugins: [await buildRouter()] } })
    await flushPromises()
    expect(w.find('a[href="/export-definitions/new"]').exists()).toBe(true)
  })

  it('shows the last run status fetched alongside the list', async () => {
    vi.spyOn(exportDefinitionsApi, 'listExportDefinitions').mockResolvedValueOnce(DEFINITIONS)
    vi.spyOn(exportDefinitionsApi, 'listExportDefinitionRuns').mockResolvedValueOnce([
      {
        id: 1,
        configVersion: 1,
        startedAt: '2026-08-27T00:00:00Z',
        finishedAt: '2026-08-27T00:00:01Z',
        status: 'Success',
        recordCount: 3,
        errorMessage: null,
        triggeredBy: 'scheduler',
        isTestRun: false,
      },
    ])
    const w = mount(ExportDefinitionsView, { global: { plugins: [await buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('Success')
  })

  it('toggles the enabled state via the checkbox', async () => {
    vi.spyOn(exportDefinitionsApi, 'listExportDefinitions').mockResolvedValueOnce(DEFINITIONS)
    const enableSpy = vi
      .spyOn(exportDefinitionsApi, 'setExportDefinitionEnabled')
      .mockResolvedValueOnce({ ok: true, data: { ...DEFINITIONS[0]!, isEnabled: true, rootNode: {} as never } })
    const w = mount(ExportDefinitionsView, { global: { plugins: [await buildRouter()] } })
    await flushPromises()

    await w.find('input[type="checkbox"]').setValue(true)
    await flushPromises()

    expect(enableSpy).toHaveBeenCalledWith(1, true)
  })

  it('duplicates a definition and reloads the list', async () => {
    const listSpy = vi.spyOn(exportDefinitionsApi, 'listExportDefinitions').mockResolvedValue(DEFINITIONS)
    const duplicateSpy = vi
      .spyOn(exportDefinitionsApi, 'duplicateExportDefinition')
      .mockResolvedValueOnce({ ok: true, data: { ...DEFINITIONS[0]!, id: 2, rootNode: {} as never } })
    const w = mount(ExportDefinitionsView, { global: { plugins: [await buildRouter()] } })
    await flushPromises()

    const duplicateBtn = w.findAll('button').find((b) => b.text() === 'Duplicate')!
    await duplicateBtn.trigger('click')
    await flushPromises()

    expect(duplicateSpy).toHaveBeenCalledWith(1)
    expect(listSpy).toHaveBeenCalledTimes(2)
  })

  it('deletes a definition after confirmation', async () => {
    const listSpy = vi.spyOn(exportDefinitionsApi, 'listExportDefinitions').mockResolvedValue(DEFINITIONS)
    const deleteSpy = vi.spyOn(exportDefinitionsApi, 'deleteExportDefinition').mockResolvedValueOnce(true)
    const w = mount(ExportDefinitionsView, { global: { plugins: [await buildRouter()] } })
    await flushPromises()

    await w.findAll('button').find((b) => b.text() === 'Delete')!.trigger('click')
    await flushPromises()
    expect(w.findAll('button').some((b) => b.text() === 'Confirm')).toBe(true)

    await w.findAll('button').find((b) => b.text() === 'Confirm')!.trigger('click')
    await flushPromises()

    expect(deleteSpy).toHaveBeenCalledWith(1)
    expect(listSpy).toHaveBeenCalledTimes(2)
  })
})
