import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import ExportDefinitionRunsTable from '@/components/ExportDefinitionRunsTable.vue'
import ImportDefinitionRunsTable from '@/components/ImportDefinitionRunsTable.vue'
import type { ExportDefinitionRun } from '@/api/exportDefinitions'
import type { ImportDefinitionRun } from '@/api/importDefinitions'

const exportRun = (over: Partial<ExportDefinitionRun>): ExportDefinitionRun => ({
  id: 1,
  configVersion: 3,
  startedAt: '2026-10-01T06:00:00Z',
  finishedAt: '2026-10-01T06:00:05Z',
  status: 'Success',
  recordCount: 3,
  errorMessage: null,
  triggeredBy: 'scheduler',
  isTestRun: false,
  dataFileName: null,
  sha256: null,
  ...over,
})

describe('ExportDefinitionRunsTable', () => {
  it('shows the staged file of a scheduled run, with its SHA-256 on hover, and a dash otherwise', () => {
    const runs = [
      exportRun({ id: 1, dataFileName: 'Alignment_Export_20261001T060000Z.json', sha256: 'abc123' }),
      exportRun({ id: 2, triggeredBy: 'alice' }),
    ]
    const w = mount(ExportDefinitionRunsTable, { props: { runs, loading: false, error: null } })

    expect(w.text()).toContain('Staged file')
    const cell = w.find('td[title="abc123"]')
    expect(cell.text()).toBe('Alignment_Export_20261001T060000Z.json')
    expect(w.findAll('tbody tr')[1].text()).toContain('—')
  })

  it('shows the empty message when there are no runs', () => {
    const w = mount(ExportDefinitionRunsTable, { props: { runs: [], loading: false, error: null } })
    expect(w.text()).toContain('No runs yet.')
    expect(w.find('table').exists()).toBe(false)
  })
})

describe('ImportDefinitionRunsTable', () => {
  it('shows the inserted count column', () => {
    const run: ImportDefinitionRun = {
      id: 7,
      configVersion: 1,
      startedAt: '2026-10-01T06:00:00Z',
      finishedAt: null,
      status: 'PendingReview',
      recordCount: 3,
      matchedCount: 1,
      changedCount: 1,
      unchangedCount: 0,
      rejectedCount: 0,
      conflictCount: 0,
      invalidCount: 0,
      errorMessage: null,
      triggeredBy: 'watcher',
      operatedBy: null,
      approvedBy: null,
      releasedAt: null,
      insertCount: 2,
    }
    const w = mount(ImportDefinitionRunsTable, { props: { runs: [run], loading: false, error: null } })

    const headers = w.findAll('th').map((th) => th.text())
    const cells = w.findAll('tbody td').map((td) => td.text())
    expect(cells[headers.indexOf('Inserted')]).toBe('2')
  })
})
