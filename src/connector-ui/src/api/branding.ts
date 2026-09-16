import { authHeaders } from './auth'

export interface BrandingConfig {
  appName: string | null
  logoDataUrl: string | null
  faviconDataUrl: string | null
  backgroundImageDataUrl: string | null
}

/** Unauthenticated — the login screen and browser tab need this before sign-in. */
export async function getBranding(): Promise<BrandingConfig> {
  const res = await fetch('/api/branding')
  if (!res.ok) throw new Error(`Failed to load branding (HTTP ${res.status})`)
  return res.json() as Promise<BrandingConfig>
}

export async function saveBranding(
  config: BrandingConfig,
): Promise<{ ok: true; config: BrandingConfig } | { ok: false; error: string }> {
  const res = await fetch('/api/branding', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json', ...authHeaders() },
    body: JSON.stringify(config),
  })
  if (res.ok) return { ok: true, config: (await res.json()) as BrandingConfig }
  const text = await res.text().catch(() => '')
  return { ok: false, error: text || `Server error (HTTP ${res.status})` }
}
