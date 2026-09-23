let pending: Promise<string> | null = null

/** Unauthenticated — the login screen shows the deployed version before sign-in. Fetched once and
 * shared, so the login card and the app footer don't each hit the API. */
export function getVersion(): Promise<string> {
  pending ??= fetch('/api/version').then(async (res) => {
    if (!res.ok) throw new Error(`Failed to load version (HTTP ${res.status})`)
    return ((await res.json()) as { version: string }).version
  })
  return pending
}
