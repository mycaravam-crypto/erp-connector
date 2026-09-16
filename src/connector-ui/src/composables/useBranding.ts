import { ref } from 'vue'
import { getBranding, type BrandingConfig } from '@/api/branding'
import defaultLogo from '@/assets/logo.svg'

const DEFAULT_APP_NAME = 'X5 Connector'
const DEFAULT_FAVICON = '/favicon.svg'

/** Shared app-wide branding state, applied as a side effect (title, favicon link, background CSS
 * variable) the same way `useTheme` applies the dark-mode class — one module-level source of truth
 * so every consumer (nav bar, login screen, settings form) sees the same values without prop-drilling. */
const appName = ref(DEFAULT_APP_NAME)
const logoUrl = ref<string>(defaultLogo)
const backgroundImageUrl = ref<string | null>(null)
let hasLoaded = false

function applyFavicon(dataUrl: string | null) {
  let link = document.querySelector<HTMLLinkElement>('link[rel="icon"]')
  if (!link) {
    link = document.createElement('link')
    link.rel = 'icon'
    document.head.appendChild(link)
  }
  link.href = dataUrl ?? DEFAULT_FAVICON
}

function applyBackground(dataUrl: string | null) {
  backgroundImageUrl.value = dataUrl
  if (dataUrl) {
    document.documentElement.style.setProperty('--x-custom-bg-image', `url("${dataUrl}")`)
  } else {
    document.documentElement.style.removeProperty('--x-custom-bg-image')
  }
}

function apply(config: BrandingConfig) {
  appName.value = config.appName?.trim() || DEFAULT_APP_NAME
  logoUrl.value = config.logoDataUrl || defaultLogo
  document.title = appName.value
  applyFavicon(config.faviconDataUrl)
  applyBackground(config.backgroundImageDataUrl)
}

async function load() {
  try {
    apply(await getBranding())
  } catch {
    // Branding is cosmetic — never block the app on it; defaults stay in effect.
  }
}

/** Shared branding state. First call kicks off the load; later calls (including after saving new
 * branding in Settings) reuse the same refs and can force a fresh `reload()`. */
export function useBranding() {
  if (!hasLoaded) {
    hasLoaded = true
    load()
  }
  return { appName, logoUrl, backgroundImageUrl, reload: load }
}
