<script setup lang="ts">
import { ref } from 'vue'
import { saveBranding, type BrandingConfig } from '@/api/branding'
import { useBranding } from '@/composables/useBranding'
import Input from '@/components/ui/Input.vue'
import Button from '@/components/ui/Button.vue'
import HelpTooltip from '@/components/ui/HelpTooltip.vue'
import SaveStatusAlert from '@/components/ui/SaveStatusAlert.vue'
import BrandingImageField from '@/components/BrandingImageField.vue'
import { useToasts } from '@/composables/useToasts'
import { useSaveStatus } from '@/composables/useSaveStatus'

const toasts = useToasts()
const { reload: reloadBranding } = useBranding()

const props = defineProps<{ config: BrandingConfig }>()

const appName = ref(props.config.appName ?? '')
const logoDataUrl = ref(props.config.logoDataUrl)
const faviconDataUrl = ref(props.config.faviconDataUrl)
const backgroundImageDataUrl = ref(props.config.backgroundImageDataUrl)

const { saving, saveStatus, saveMessage, reset: resetSaveStatus } = useSaveStatus()

async function save() {
  saving.value = true
  resetSaveStatus()
  try {
    const result = await saveBranding({
      appName: appName.value.trim() || null,
      logoDataUrl: logoDataUrl.value,
      faviconDataUrl: faviconDataUrl.value,
      backgroundImageDataUrl: backgroundImageDataUrl.value,
    })
    if (result.ok) {
      saveStatus.value = 'ok'
      saveMessage.value = 'Branding saved.'
      toasts.success(saveMessage.value)
      await reloadBranding()
    } else {
      saveStatus.value = 'error'
      saveMessage.value = result.error ?? 'Unknown error.'
      toasts.error(saveMessage.value)
    }
  } catch {
    saveStatus.value = 'error'
    saveMessage.value = 'Could not reach the backend. Is the backend service running?'
    toasts.error(saveMessage.value)
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <section class="mt-10">
    <span class="inline-flex items-center gap-1.5 mb-1">
      <h2 class="text-base font-semibold text-text-primary m-0">Branding</h2>
      <HelpTooltip label="What does this change?" title="White-label the connector for your organization">
        <p>
          Replace the default name, logo, browser tab icon, and page background with your own —
          applied everywhere in this UI, including the login screen, for every user.
        </p>
        <p>Leave a field empty (or click Remove) to fall back to the built-in default.</p>
      </HelpTooltip>
    </span>
    <p class="text-text-secondary text-sm mb-4 leading-relaxed">
      Changes apply immediately for everyone after saving.
    </p>

    <form class="flex flex-col gap-4 max-w-xl" @submit.prevent="save">
      <Input
        id="branding-app-name"
        v-model="appName"
        label="Application name"
        placeholder="X5 Connector"
        :maxlength="60"
        help-text="Shown in the nav bar, login screen, and browser tab title."
      />

      <BrandingImageField
        v-model="logoDataUrl"
        label="Logo"
        accept="image/png,image/jpeg,image/svg+xml,image/webp"
        :max-bytes="2 * 1024 * 1024"
        help-text="Square image, shown in the nav bar and login screen. PNG, JPEG, WebP, or SVG, up to 2MB."
      />

      <BrandingImageField
        v-model="faviconDataUrl"
        label="Favicon"
        accept="image/png,image/svg+xml,image/x-icon,image/vnd.microsoft.icon"
        :max-bytes="512 * 1024"
        help-text="Browser tab icon. PNG, SVG, or ICO, up to 512KB."
      />

      <BrandingImageField
        v-model="backgroundImageDataUrl"
        label="Background image"
        accept="image/png,image/jpeg,image/webp"
        :max-bytes="4 * 1024 * 1024"
        preview-shape="wide"
        help-text="Shown behind every page, scaled to cover the viewport. PNG, JPEG, or WebP, up to 4MB."
      />

      <div>
        <Button type="submit" variant="secondary" :loading="saving">
          {{ saving ? 'Saving…' : 'Save Branding' }}
        </Button>
      </div>
    </form>

    <SaveStatusAlert :status="saveStatus" :message="saveMessage" />
  </section>
</template>
