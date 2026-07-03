import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

/** This browser's real notification consent — mirrors the browser's `Notification.permission`. */
export type NotifPermission = 'default' | 'granted' | 'denied';

/** One registered device row for the Settings list (backed by `GET /api/push/devices`). */
export interface NotifDeviceRow {
  id: string;
  /** Human-readable device label (UA-derived at subscribe time). */
  label: string;
  /** The push endpoint — used to match/flag the current browser and to unsubscribe. */
  endpoint: string;
  /** True for this browser's device (gets the THIS DEVICE badge). */
  isCurrent: boolean;
}

/** `GET /api/push/key` — the public VAPID key, or a disabled marker when the server has no keypair. */
interface PushKeyResponse {
  publicKey: string | null;
  enabled: boolean;
}

/** `GET /api/push/devices` row shape. */
interface PushDeviceResponse {
  id: string;
  label: string | null;
  endpoint: string;
  createdAtUtc: string;
}

const DEVICE_LABEL_FALLBACK = 'Unknown device';

/**
 * Real Web Push adapter (real-web-push) — replaces the former `localStorage` simulation. Thin wrapper over
 * the browser Push APIs (`Notification`, the Service Worker registration, `PushManager`) and the backend
 * registry (`/api/push/*`). The device list is server-side, so it is identical across every browser the
 * account signs into (the bug the simulation had). Activation is **phone-only** (product decision): the
 * enable affordance requires both a push-capable browser ({@link supported}) and a phone ({@link isMobile},
 * UA-based) — {@link canActivate}. Desktop can't activate; it shows the device list + a QR to enable from a
 * phone.
 */
@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly http = inject(HttpClient);

  /** Real Web Push needs a Service Worker, the Push API, and the Notification API. */
  readonly supported =
    typeof navigator !== 'undefined' &&
    'serviceWorker' in navigator &&
    typeof window !== 'undefined' &&
    'PushManager' in window &&
    'Notification' in window;

  /** Whether this is a phone (UA-based). Push activation is phone-only; desktop only informs + shows the QR. */
  readonly isMobile = isMobileUserAgent();

  /** The enable affordance is offered only on a push-capable phone. */
  readonly canActivate = this.supported && this.isMobile;

  private readonly _permission = signal<NotifPermission>(this.readPermission());
  readonly permission = this._permission.asReadonly();

  /** This browser's current subscription endpoint (null when not subscribed) — flags the current device. */
  private readonly _currentEndpoint = signal<string | null>(null);
  readonly hasCurrentSubscription = computed(() => this._currentEndpoint() !== null);

  private readonly _devices = signal<NotifDeviceRow[]>([]);

  /** The device rows Settings renders, each flagged `isCurrent` by matching this browser's endpoint. */
  readonly deviceList = computed<NotifDeviceRow[]>(() => {
    const current = this._currentEndpoint();
    return this._devices().map((d) => ({ ...d, isCurrent: d.endpoint === current }));
  });

  /** Whether any device is registered (every listed device is an active server-side subscription). */
  readonly anyEnabled = computed(() => this._devices().length > 0);

  /** Account-status line for Settings; keyed on how many devices are subscribed. */
  readonly notifStatusText = computed(() => {
    const count = this._devices().length;
    if (count === 0) {
      return "Notifications aren't on for any of your devices yet.";
    }
    return count === 1
      ? 'Notifications are on for 1 device.'
      : `Notifications are on for ${count} devices.`;
  });

  /** Session-scoped (not persisted): the Board soft-ask may return on a later visit. */
  private readonly _softAskDismissed = signal(false);
  readonly softAskDismissed = this._softAskDismissed.asReadonly();

  constructor() {
    // Local-only (no network / no auth): learn whether THIS browser already holds a subscription.
    if (this.supported) {
      void this.refreshCurrentEndpoint();
    }
  }

  /** Dismiss the Board soft-ask for this session only. */
  dismissSoftAsk(): void {
    this._softAskDismissed.set(true);
  }

  /**
   * User-initiated activation: request real permission, and on grant subscribe this browser via
   * `PushManager` and persist the subscription to the backend registry. No-op on an unsupported browser,
   * a denied prompt, or when the server has no VAPID key configured.
   */
  async enable(): Promise<void> {
    // Phone-only: desktop never subscribes (it shows the QR instead).
    if (!this.canActivate) {
      return;
    }

    const permission = await Notification.requestPermission();
    this._permission.set(permission as NotifPermission);
    if (permission !== 'granted') {
      return;
    }

    const registration = await navigator.serviceWorker.ready;
    const key = await firstValueFrom(this.http.get<PushKeyResponse>('/api/push/key'));
    if (!key?.enabled || !key.publicKey) {
      // Server push isn't configured — nothing to subscribe against; degrade gracefully.
      return;
    }

    const subscription = await registration.pushManager.subscribe({
      userVisibleOnly: true,
      applicationServerKey: urlBase64ToUint8Array(key.publicKey),
    });

    const json = subscription.toJSON();
    await firstValueFrom(
      this.http.post('/api/push/subscribe', {
        endpoint: subscription.endpoint,
        keys: { p256dh: json.keys?.['p256dh'], auth: json.keys?.['auth'] },
        deviceLabel: describeDevice(),
      }),
    );

    this._currentEndpoint.set(subscription.endpoint);
    await this.refreshDevices();
  }

  /**
   * Remove a device from the registry. For this browser's own subscription it also tears down the local
   * `PushManager` subscription; for any other device it just deletes the server row (a remote browser's
   * `PushManager` can only be unsubscribed on that browser).
   */
  async disable(endpoint?: string): Promise<void> {
    let current: PushSubscription | null = null;
    if (this.supported) {
      const registration = await navigator.serviceWorker.ready;
      current = await registration.pushManager.getSubscription();
    }

    const target = endpoint ?? current?.endpoint;
    if (!target) {
      return;
    }

    if (current && current.endpoint === target) {
      await current.unsubscribe().catch(() => undefined);
      this._currentEndpoint.set(null);
    }

    await firstValueFrom(
      this.http.request('delete', '/api/push/subscribe', { body: { endpoint: target } }),
    );
    await this.refreshDevices();
  }

  /** Reload the account's device list from the backend (the source of truth for the Settings list). */
  async refreshDevices(): Promise<void> {
    try {
      const devices = await firstValueFrom(this.http.get<PushDeviceResponse[]>('/api/push/devices'));
      this._devices.set(
        (devices ?? []).map((d) => ({
          id: d.id,
          label: d.label ?? DEVICE_LABEL_FALLBACK,
          endpoint: d.endpoint,
          isCurrent: false,
        })),
      );
    } catch {
      // Not signed in / offline — leave the current list untouched.
    }
  }

  private async refreshCurrentEndpoint(): Promise<void> {
    try {
      const registration = await navigator.serviceWorker.ready;
      const subscription = await registration.pushManager.getSubscription();
      this._currentEndpoint.set(subscription?.endpoint ?? null);
    } catch {
      this._currentEndpoint.set(null);
    }
  }

  private readPermission(): NotifPermission {
    return this.supported ? (Notification.permission as NotifPermission) : 'default';
  }
}

/** UA-based phone check — push activation is phone-only (desktop only shows the QR to enable from a phone). */
function isMobileUserAgent(): boolean {
  if (typeof navigator === 'undefined' || !navigator.userAgent) {
    return false;
  }
  return /Android|iPhone|iPad|iPod|Windows Phone|BlackBerry|Opera Mini|IEMobile|Mobile/i.test(
    navigator.userAgent,
  );
}

/** Convert a base64url VAPID public key to the `Uint8Array` `PushManager.subscribe` expects. */
function urlBase64ToUint8Array(base64String: string): Uint8Array<ArrayBuffer> {
  const padding = '='.repeat((4 - (base64String.length % 4)) % 4);
  const base64 = (base64String + padding).replace(/-/g, '+').replace(/_/g, '/');
  const raw = atob(base64);
  // Allocate over a concrete ArrayBuffer so the result is a BufferSource (not ArrayBufferLike-generic).
  const output = new Uint8Array(new ArrayBuffer(raw.length));
  for (let i = 0; i < raw.length; i++) {
    output[i] = raw.charCodeAt(i);
  }
  return output;
}

/** A short human label for this device from the User-Agent (e.g. "Chrome on Windows"). */
function describeDevice(): string {
  if (typeof navigator === 'undefined' || !navigator.userAgent) {
    return DEVICE_LABEL_FALLBACK;
  }
  const ua = navigator.userAgent;

  const browser = /Edg\//.test(ua)
    ? 'Edge'
    : /OPR\/|Opera/.test(ua)
      ? 'Opera'
      : /Firefox\//.test(ua)
        ? 'Firefox'
        : /Chrome\//.test(ua)
          ? 'Chrome'
          : /Safari\//.test(ua)
            ? 'Safari'
            : 'Browser';

  const os = /Windows/.test(ua)
    ? 'Windows'
    : /Android/.test(ua)
      ? 'Android'
      : /iPhone|iPad|iPod/.test(ua)
        ? 'iOS'
        : /Mac OS X|Macintosh/.test(ua)
          ? 'macOS'
          : /Linux/.test(ua)
            ? 'Linux'
            : 'device';

  return `${browser} on ${os}`;
}
