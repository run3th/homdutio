/**
 * Registers the minimal Web Push Service Worker (`/sw.js`, scope `/`) once at bootstrap, where the browser
 * supports it. Best-effort and non-blocking: a registration failure (unsupported browser, insecure origin,
 * etc.) is swallowed so it never delays or breaks app startup. Wired in via `provideAppInitializer`.
 */
export function registerServiceWorker(): void {
  if (typeof navigator === 'undefined' || !('serviceWorker' in navigator)) {
    return;
  }

  navigator.serviceWorker.register('/sw.js').catch(() => {
    // Best-effort: notifications simply stay unavailable if registration fails.
  });
}
