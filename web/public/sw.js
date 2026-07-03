/*
 * Minimal Web Push Service Worker (real-web-push). Handles ONLY `push` and `notificationclick` — no
 * app-shell caching and no `fetch` handler, so a stale SW can never serve stale app assets (freshness is
 * left entirely to the normal HTTP layer). Served from the wwwroot root so its scope is `/`.
 */

self.addEventListener('push', (event) => {
  let payload = {};
  try {
    payload = event.data ? event.data.json() : {};
  } catch {
    payload = {};
  }

  const title = payload.title || 'Homdutio';
  const options = {
    body: payload.body || '',
    // App icon shown in the notification. PNG (not .ico) — Chrome ignores/poorly scales .ico here.
    // Payload may override; otherwise fall back to the app's own icon (never the Angular default favicon).
    icon: payload.icon || '/icon-192.png',
    // Small monochrome mask Android renders next to the app name; transparent PNG.
    badge: payload.badge || '/badge-72.png',
    data: { url: (payload.data && payload.data.url) || '/' },
  };

  event.waitUntil(self.registration.showNotification(title, options));
});

self.addEventListener('notificationclick', (event) => {
  event.notification.close();

  const url = (event.notification.data && event.notification.data.url) || '/';

  event.waitUntil(
    (async () => {
      const clientList = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });

      // Focus an already-open app window and steer it to the target, if one exists.
      for (const client of clientList) {
        if ('focus' in client) {
          await client.focus();
          if ('navigate' in client) {
            try {
              await client.navigate(url);
            } catch {
              // Navigation can be refused (e.g. mid-load); focusing is still useful.
            }
          }
          return;
        }
      }

      // Otherwise open a fresh window at the deep-link.
      if (self.clients.openWindow) {
        await self.clients.openWindow(url);
      }
    })(),
  );
});
