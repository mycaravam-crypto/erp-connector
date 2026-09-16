import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import BrandingImageField from '@/components/BrandingImageField.vue'

function fileOf(bytes: number, type = 'image/png'): File {
  return new File([new Uint8Array(bytes)], 'test.png', { type })
}

async function selectFile(w: ReturnType<typeof mount>, file: File) {
  const input = w.find('input[type="file"]').element as HTMLInputElement
  Object.defineProperty(input, 'files', { value: [file], configurable: true })
  await w.find('input[type="file"]').trigger('change')
  // FileReader.onload fires asynchronously
  await new Promise((r) => setTimeout(r, 50))
}

describe('BrandingImageField', () => {
  const baseProps = { label: 'Logo', accept: 'image/png', maxBytes: 1024 }

  it('shows "None" when no image is set', () => {
    const w = mount(BrandingImageField, { props: { ...baseProps, modelValue: null } })
    expect(w.text()).toContain('None')
    expect(w.find('button[aria-label], button').exists()).toBe(true)
  })

  it('renders a preview and Remove button when a value is set', () => {
    const w = mount(BrandingImageField, {
      props: { ...baseProps, modelValue: 'data:image/png;base64,abc' },
    })
    expect(w.find('img').attributes('src')).toBe('data:image/png;base64,abc')
    expect(w.findAll('button').some((b) => b.text().includes('Remove'))).toBe(true)
  })

  it('rejects a non-image file', async () => {
    const w = mount(BrandingImageField, { props: { ...baseProps, modelValue: null } })
    await selectFile(w, new File(['x'], 'test.txt', { type: 'text/plain' }))
    expect(w.text()).toContain('Please choose an image file.')
    expect(w.emitted('update:modelValue')).toBeUndefined()
  })

  it('rejects a file larger than maxBytes', async () => {
    const w = mount(BrandingImageField, { props: { ...baseProps, modelValue: null } })
    await selectFile(w, fileOf(2048))
    expect(w.text()).toContain('too large')
    expect(w.emitted('update:modelValue')).toBeUndefined()
  })

  it('reads a valid file as a data URL and emits it', async () => {
    const w = mount(BrandingImageField, { props: { ...baseProps, modelValue: null } })
    await selectFile(w, fileOf(10))
    const emitted = w.emitted('update:modelValue')
    expect(emitted).toBeTruthy()
    expect((emitted![0][0] as string).startsWith('data:image/png;base64,')).toBe(true)
  })

  it('clears the value when Remove is clicked', async () => {
    const w = mount(BrandingImageField, {
      props: { ...baseProps, modelValue: 'data:image/png;base64,abc' },
    })
    const removeButton = w.findAll('button').find((b) => b.text().includes('Remove'))!
    await removeButton.trigger('click')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([null])
  })
})
