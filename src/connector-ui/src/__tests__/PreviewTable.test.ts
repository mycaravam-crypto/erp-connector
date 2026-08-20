import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import PreviewTable from '@/components/PreviewTable.vue'
import type { PreviewResult } from '@/api/pipeline'

const FLAT_PREVIEW: PreviewResult = {
  recordCount: 2,
  schemaVersion: '2.0',
  columns: ['guid', 'serial_number'],
  records: [
    { guid: 'a1', serial_number: 'SN-001' },
    { guid: 'a2', serial_number: 'SN-002' },
  ],
  source: 'dynamic',
}

const NESTED_PREVIEW: PreviewResult = {
  recordCount: 1,
  schemaVersion: '2.0',
  columns: [],
  records: [],
  source: 'dynamic-nested',
  nestedRecords: [{ id: 'a1', manufacturer: { name: 'ACME', addresses: [{ city: 'Berlin' }] } }],
}

describe('PreviewTable', () => {
  it('shows loading state', () => {
    const w = mount(PreviewTable, { props: { preview: null, loading: true, error: null } })
    expect(w.text()).toContain('Loading preview')
  })

  it('shows fetch error state', () => {
    const w = mount(PreviewTable, {
      props: { preview: null, loading: false, error: 'Could not reach the API.' },
    })
    expect(w.text()).toContain('Could not reach the API.')
  })

  it('shows preview-failed state from source === "error"', () => {
    const preview: PreviewResult = {
      recordCount: 0,
      schemaVersion: '2.0',
      columns: [],
      records: [],
      source: 'error',
      error: 'No export mapping configured.',
    }
    const w = mount(PreviewTable, { props: { preview, loading: false, error: null } })
    expect(w.text()).toContain('Preview failed')
    expect(w.text()).toContain('No export mapping configured.')
  })

  it('renders the flat table for a flat mapping', () => {
    const w = mount(PreviewTable, { props: { preview: FLAT_PREVIEW, loading: false, error: null } })
    expect(w.text()).toContain('guid')
    expect(w.text()).toContain('SN-001')
    expect(w.text()).not.toContain('manufacturer')
  })

  it('shows empty state when a flat mapping returns zero records', () => {
    const preview: PreviewResult = { ...FLAT_PREVIEW, recordCount: 0, records: [] }
    const w = mount(PreviewTable, { props: { preview, loading: false, error: null } })
    expect(w.text()).toContain('No in-scope records found.')
  })

  it('renders nested JSON records for a nested-group mapping', () => {
    const w = mount(PreviewTable, { props: { preview: NESTED_PREVIEW, loading: false, error: null } })
    expect(w.text()).toContain('"manufacturer"')
    expect(w.text()).toContain('"ACME"')
    expect(w.text()).toContain('Berlin')
    expect(w.find('table').exists()).toBe(false)
  })

  it('shows empty state when a nested mapping returns zero records', () => {
    const preview: PreviewResult = { ...NESTED_PREVIEW, recordCount: 0, nestedRecords: [] }
    const w = mount(PreviewTable, { props: { preview, loading: false, error: null } })
    expect(w.text()).toContain('No in-scope records found.')
  })

  it('emits refresh when the Refresh button is clicked', async () => {
    const w = mount(PreviewTable, { props: { preview: FLAT_PREVIEW, loading: false, error: null } })
    await w.find('button').trigger('click')
    expect(w.emitted('refresh')).toBeTruthy()
  })
})
