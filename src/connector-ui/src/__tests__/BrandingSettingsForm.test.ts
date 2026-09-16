import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import BrandingSettingsForm from '@/components/BrandingSettingsForm.vue'
import * as brandingApi from '@/api/branding'
import { useToasts } from '@/composables/useToasts'

const emptyConfig: brandingApi.BrandingConfig = {
  appName: null,
  logoDataUrl: null,
  faviconDataUrl: null,
  backgroundImageDataUrl: null,
}

beforeEach(() => {
  vi.restoreAllMocks()
  useToasts().clear()
})

describe('BrandingSettingsForm', () => {
  it('pre-fills the app name from the initial config', () => {
    const w = mount(BrandingSettingsForm, {
      props: { config: { ...emptyConfig, appName: 'Acme Connector' } },
    })
    expect((w.find('input#branding-app-name').element as HTMLInputElement).value).toBe(
      'Acme Connector',
    )
  })

  it('saves the current field values', async () => {
    const spy = vi.spyOn(brandingApi, 'saveBranding').mockResolvedValueOnce({
      ok: true,
      config: emptyConfig,
    })
    const w = mount(BrandingSettingsForm, { props: { config: emptyConfig } })
    await w.find('input#branding-app-name').setValue('My ERP')
    await w.find('form').trigger('submit')
    expect(spy).toHaveBeenCalledWith({
      appName: 'My ERP',
      logoDataUrl: null,
      faviconDataUrl: null,
      backgroundImageDataUrl: null,
    })
  })

  it('pushes a success toast on save', async () => {
    vi.spyOn(brandingApi, 'saveBranding').mockResolvedValueOnce({ ok: true, config: emptyConfig })
    const w = mount(BrandingSettingsForm, { props: { config: emptyConfig } })
    await w.find('form').trigger('submit')
    await w.vm.$nextTick()
    expect(useToasts().toasts.value.some((t) => t.variant === 'success')).toBe(true)
  })

  it('shows an error message when the save fails', async () => {
    vi.spyOn(brandingApi, 'saveBranding').mockResolvedValueOnce({ ok: false, error: 'too big' })
    const w = mount(BrandingSettingsForm, { props: { config: emptyConfig } })
    await w.find('form').trigger('submit')
    await w.vm.$nextTick()
    await new Promise((r) => setTimeout(r, 0))
    expect(w.text()).toContain('too big')
  })

  it('sends an empty app name as null so the backend falls back to the default', async () => {
    const spy = vi.spyOn(brandingApi, 'saveBranding').mockResolvedValueOnce({
      ok: true,
      config: emptyConfig,
    })
    const w = mount(BrandingSettingsForm, {
      props: { config: { ...emptyConfig, appName: 'Old Name' } },
    })
    await w.find('input#branding-app-name').setValue('   ')
    await w.find('form').trigger('submit')
    expect(spy).toHaveBeenCalledWith(expect.objectContaining({ appName: null }))
  })
})
