import { Injectable, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { BreakpointObserver } from '@angular/cdk/layout';
import { Dialog } from '@angular/cdk/dialog';
import { map } from 'rxjs';

import { AuthService } from '../auth/auth.service';
import { FlashService } from '../shared/flash/flash.service';
import { SystemPromptComponent } from './system-prompt/system-prompt.component';

/** This device's simulated notification consent — mirrors the browser's `Notification.permission`. */
export type NotifPermission = 'default' | 'granted' | 'denied';

/**
 * A device registered to the account — one only exists once it has turned notifications on at least once
 * (i.e. "subscribed"). `enabled` mirrors web-push reality: `true` = active subscription (On), `false` = a
 * once-on device whose consent was later revoked (Off). We never fabricate Off rows for devices that never
 * subscribed. Persisted (account-wide simulation) in `localStorage['homdutio_devices']`.
 */
export interface RegisteredDevice {
  id: string;
  name: string;
  enabled: boolean;
}

/** A device row the Settings section renders (registered devices, plus a synthetic activation row on a phone). */
export interface NotifDeviceRow {
  id: string;
  name: string;
  enabled: boolean;
  /** True for this browser's device (gets the THIS DEVICE badge). */
  isCurrent: boolean;
  /** The only in-list activation affordance: this phone when it isn't currently subscribed. */
  showEnable: boolean;
}

/** `localStorage` keys — consent is per-device; the device registry stands in for the account. */
const PERM_KEY = 'homdutio_notif_perm';
const DEVICES_KEY = 'homdutio_devices';
const DEVICE_ID_KEY = 'homdutio_device_id';
/** The 999px cutoff (reference `isMobile = width < 1000`): at/below is a phone that *can* activate. */
const MOBILE_QUERY = '(max-width: 999px)';

/**
 * Single source of truth for the *simulated* per-device notification state (push-notifications). No real
 * Service Worker / Web Push / backend registry exists — this device's consent lives in `localStorage`, and
 * the account's device registry (devices that have subscribed) lives in `localStorage['homdutio_devices']`.
 * A device appears in Settings only once it has turned notifications on; an Off row means a once-subscribed
 * device whose consent was revoked. The load-bearing rule is applied in one place: {@link pushNotify}
 * delivers only when `isMobile && permission === 'granted'` (a push lands only on a device that turned it on).
 */
@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly breakpoints = inject(BreakpointObserver);
  private readonly dialog = inject(Dialog);
  private readonly flash = inject(FlashService);
  private readonly auth = inject(AuthService);

  /** A stable per-browser id so this device can be matched against the persisted registry across reloads. */
  private readonly currentDeviceId = this.readOrCreateDeviceId();

  /** This device's consent, seeded from `localStorage` on construction and persisted on every change. */
  private readonly _permission = signal<NotifPermission>(this.readPermission());
  readonly permission = this._permission.asReadonly();

  /**
   * Whether the viewport is phone-sized (reference's `isMobile`). Desktop never activates — it only informs.
   * `initialValue` uses a synchronous match so the first read is correct before the observable ticks.
   */
  readonly isMobile = toSignal(this.breakpoints.observe(MOBILE_QUERY).pipe(map((s) => s.matches)), {
    initialValue: this.breakpoints.isMatched(MOBILE_QUERY),
  });

  /** The account's registered devices (those that have subscribed at least once), from `localStorage`. */
  private readonly _devices = signal<RegisteredDevice[]>(this.readDevices());

  /** Session-scoped (not persisted): the soft-ask/desktop banner may return on a later visit. */
  private readonly _softAskDismissed = signal(false);
  readonly softAskDismissed = this._softAskDismissed.asReadonly();

  /**
   * The device rows Settings renders. Registered devices always show (On, or Off if revoked). On a phone that
   * hasn't subscribed yet, a synthetic "This phone · Off" activation row leads the list so it can be turned
   * on; on desktop no synthetic row is added (desktop can't be a push device), so an empty registry = no rows.
   */
  readonly deviceList = computed<NotifDeviceRow[]>(() => {
    const devices = this._devices();
    const rows: NotifDeviceRow[] = devices.map((d) => ({
      id: d.id,
      name: d.name,
      enabled: d.enabled,
      isCurrent: d.id === this.currentDeviceId,
      showEnable: d.id === this.currentDeviceId && !d.enabled,
    }));

    const currentRegistered = devices.some((d) => d.id === this.currentDeviceId);
    if (this.isMobile() && !currentRegistered) {
      rows.unshift({
        id: this.currentDeviceId,
        name: 'This phone',
        enabled: false,
        isCurrent: true,
        showEnable: true,
      });
    }
    return rows;
  });

  /** Whether notifications are on for any registered device (drives the desktop banner's `!anyEnabled` gate). */
  readonly anyEnabled = computed(() => this._devices().some((d) => d.enabled));

  /** Account-status line for Settings; two variants keyed on how many devices are subscribed. */
  readonly notifStatusText = computed(() => {
    const on = this._devices().filter((d) => d.enabled).length;
    if (on === 0) {
      return "Notifications aren't on for any of your devices yet.";
    }
    return on === 1
      ? 'Notifications are on for 1 device.'
      : `Notifications are on for ${on} devices.`;
  });

  /**
   * User-initiated activation. No-op on desktop (can't activate) or when already `granted`; otherwise opens
   * the simulated OS prompt. Never call any real permission API, and never open the prompt without a click.
   */
  requestNotifs(): void {
    if (!this.isMobile() || this._permission() === 'granted') {
      return;
    }
    this.dialog.open(SystemPromptComponent);
  }

  /** Simulated "Allow" — persist consent and register/re-enable this device's subscription. */
  grant(): void {
    this.setPermission('granted');
    this.subscribeCurrentDevice();
  }

  /** Simulated "Don't Allow" — persist the denial; if this device had subscribed, mark it revoked (Off). */
  deny(): void {
    this.setPermission('denied');
    this.revokeCurrentDevice();
  }

  /** Dismiss the soft-ask/desktop banner for this session only. */
  dismissSoftAsk(): void {
    this._softAskDismissed.set(true);
  }

  /**
   * Deliver a simulated push toast — but ONLY to a device that turned notifications on itself
   * (`isMobile && permission === 'granted'`). This single gate is what makes push "per-device".
   */
  pushNotify(title: string, body: string): void {
    if (!(this.isMobile() && this._permission() === 'granted')) {
      return;
    }
    this.flash.push(title, body);
  }

  /** Register this device (or re-enable it) as an active subscription in the account registry. */
  private subscribeCurrentDevice(): void {
    const devices = this._devices();
    const existing = devices.find((d) => d.id === this.currentDeviceId);
    const next = existing
      ? devices.map((d) => (d.id === this.currentDeviceId ? { ...d, enabled: true } : d))
      : [...devices, { id: this.currentDeviceId, name: this.currentDeviceName(), enabled: true }];
    this.setDevices(next);
  }

  /** Mark this device's subscription revoked (Off) — but only if it had ever subscribed (no fabricated rows). */
  private revokeCurrentDevice(): void {
    const devices = this._devices();
    if (!devices.some((d) => d.id === this.currentDeviceId)) {
      return;
    }
    this.setDevices(devices.map((d) => (d.id === this.currentDeviceId ? { ...d, enabled: false } : d)));
  }

  /** This device's name for the registry — "<display name>'s phone", or "This phone" before `/me` resolves. */
  private currentDeviceName(): string {
    const name = this.auth.displayName();
    return name ? `${name}'s phone` : 'This phone';
  }

  private setPermission(permission: NotifPermission): void {
    this._permission.set(permission);
    localStorage.setItem(PERM_KEY, permission);
  }

  private setDevices(devices: RegisteredDevice[]): void {
    this._devices.set(devices);
    localStorage.setItem(DEVICES_KEY, JSON.stringify(devices));
  }

  private readPermission(): NotifPermission {
    const stored = localStorage.getItem(PERM_KEY);
    return stored === 'granted' || stored === 'denied' ? stored : 'default';
  }

  private readDevices(): RegisteredDevice[] {
    try {
      const raw = localStorage.getItem(DEVICES_KEY);
      const parsed: unknown = raw ? JSON.parse(raw) : [];
      if (!Array.isArray(parsed)) {
        return [];
      }
      return parsed.filter(
        (d): d is RegisteredDevice =>
          !!d &&
          typeof d.id === 'string' &&
          typeof d.name === 'string' &&
          typeof d.enabled === 'boolean',
      );
    } catch {
      return [];
    }
  }

  private readOrCreateDeviceId(): string {
    const stored = localStorage.getItem(DEVICE_ID_KEY);
    if (stored) {
      return stored;
    }
    const id =
      typeof crypto !== 'undefined' && 'randomUUID' in crypto
        ? crypto.randomUUID()
        : `dev-${Date.now().toString(36)}`;
    localStorage.setItem(DEVICE_ID_KEY, id);
    return id;
  }
}
