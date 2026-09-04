import { ref, watch } from 'vue'

export type ThemePreference = 'light' | 'dark' | 'system'

const STORAGE_KEY = 'x5-connector-theme'

function readStoredPreference(): ThemePreference {
  try {
    const stored = localStorage.getItem(STORAGE_KEY)
    return stored === 'light' || stored === 'dark' ? stored : 'system'
  } catch {
    return 'system'
  }
}

function systemPrefersDark(): boolean {
  return window.matchMedia('(prefers-color-scheme: dark)').matches
}

function applyTheme(pref: ThemePreference) {
  const isDark = pref === 'dark' || (pref === 'system' && systemPrefersDark())
  document.documentElement.classList.toggle('dark', isDark)
}

const preference = ref<ThemePreference>(readStoredPreference())

if (typeof window !== 'undefined') {
  applyTheme(preference.value)

  window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => {
    if (preference.value === 'system') applyTheme('system')
  })

  watch(preference, (pref) => {
    applyTheme(pref)
    try {
      if (pref === 'system') localStorage.removeItem(STORAGE_KEY)
      else localStorage.setItem(STORAGE_KEY, pref)
    } catch {
      // localStorage unavailable (e.g. private browsing) — theme still applies for this load
    }
  })
}

/** Shared app-wide theme preference: 'light' | 'dark' follow a manual choice, 'system' follows prefers-color-scheme. */
export function useTheme() {
  function setPreference(pref: ThemePreference) {
    preference.value = pref
  }

  function toggle() {
    const isDark = document.documentElement.classList.contains('dark')
    setPreference(isDark ? 'light' : 'dark')
  }

  return { preference, setPreference, toggle }
}
