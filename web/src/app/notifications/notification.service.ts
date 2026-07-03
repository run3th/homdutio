import { Injectable, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { BreakpointObserver } from '@angular/cdk/layout';
import { Dialog } from '@angular/cdk/dialog';
import { map } from 'rxjs';

import { FlashService } from '../shared/flash/flash.service';
import { SystemPromptComponent } from './system-prompt/system-prompt.component';

/** This device's simulated notification consent — mirrors the browser's `Notification.permission`. */
export type NotifPermission = 'default' | 'granted' | 'denied';

/** One of the account's *other* (seeded) devices in the simulated per-device registry. */
export interface NotifDevice {
  id: string;
  name: string;
  type: 'mobile';
  enabled: boolean;
}

/** A device row the Settings section renders: the seeded devices plus (on a phone) this device. */
export interface NotifDeviceRow {
  id: string;
  name: string;
  enabled: boolean;
  /** True for the synthetic "this device" row (gets the THIS DEVICE badge). */
  isCurrent: boolean;
  /** The only in-list activation affordance: the current phone when consent isn't `granted` yet. */
  showEnable: boolean;
}

/** `localStorage` key holding this device's consent — the whole point of "per-device" is that it's local. */
const PERM_KEY = 'homdutio_notif_perm';
/** The 999px cutoff (reference `isMobile = width < 1000`): at/below is a phone that *can* activate. */
const MOBILE_QUERY = '(max-width: 999px)';

/**
 * Single source of truth for the *simulated* per-device notification state (push-notifications). No real
 * Service Worker / Web Push / backend registry exists — consent lives in `localStorage` for THIS device
 * only, "other devices" are seeded, and the OS permission prompt is a CDK dialog. The load-bearing rule
 * is applied in exactly one place: {@link pushNotify} delivers only when `isMobile && permission === 'granted'`
 * (a push lands only on a device that turned it on itself). Activation ({@link requestNotifs}) is
 * mobile-only and always user-initiated.
 */
@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly breakpoints = inject(BreakpointObserver);
  private readonly dialog = inject(Dialog);
  private readonly flash = inject(FlashService);

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

  /** The account's *other* devices (seeded — there is no backend for this). */
  private readonly _otherDevices = signal<NotifDevice[]>([
    { id: 'nd1', name: 'iPhone Rafała', type: 'mobile', enabled: false },
    { id: 'nd2', name: 'iPad kuchnia', type: 'mobile', enabled: false },
  ]);

  /** Session-scoped (not persisted): the soft-ask/desktop banner may return on a later visit. */
  private readonly _softAskDismissed = signal(false);
  readonly softAskDismissed = this._softAskDismissed.asReadonly();

  /**
   * The device rows Settings renders. On a phone the current device leads the list (its `enabled` tracks
   * consent, and it offers the sole "enable here" button until granted); on desktop the dead
   * "not supported here" current-device row is omitted entirely — only the seeded devices show.
   */
  readonly deviceList = computed<NotifDeviceRow[]>(() => {
    const others = this._otherDevices().map((d) => ({
      id: d.id,
      name: d.name,
      enabled: d.enabled,
      isCurrent: false,
      showEnable: false,
    }));

    if (!this.isMobile()) {
      return others;
    }

    const granted = this._permission() === 'granted';
    const current: NotifDeviceRow = {
      id: 'current',
      name: 'This phone',
      enabled: granted,
      isCurrent: true,
      showEnable: !granted,
    };
    return [current, ...others];
  });

  /** Whether notifications are on for any device (drives the desktop banner's `!anyEnabled` gate). */
  readonly anyEnabled = computed(() => this.deviceList().some((d) => d.enabled));

  /** Account-status line for Settings; two variants keyed on {@link anyEnabled}. */
  readonly notifStatusText = computed(() => {
    const on = this.deviceList().filter((d) => d.enabled).length;
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

  /** Simulated "Allow" — persist consent for this device. */
  grant(): void {
    this.setPermission('granted');
  }

  /** Simulated "Don't Allow" — persist the denial for this device. */
  deny(): void {
    this.setPermission('denied');
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

  private setPermission(permission: NotifPermission): void {
    this._permission.set(permission);
    localStorage.setItem(PERM_KEY, permission);
  }

  private readPermission(): NotifPermission {
    const stored = localStorage.getItem(PERM_KEY);
    return stored === 'granted' || stored === 'denied' ? stored : 'default';
  }
}
