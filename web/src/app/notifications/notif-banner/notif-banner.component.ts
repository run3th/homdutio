import { Component, OnInit, computed, inject } from '@angular/core';
import { Dialog } from '@angular/cdk/dialog';

import { NotificationService } from '../notification.service';
import { DenyHelpComponent } from '../deny-help/deny-help.component';

/**
 * The Board's notification banner (real-web-push), sitting above the columns. Push is phone-only, so the
 * banner keeps two variants, all derived from {@link NotificationService}:
 * - **soft-ask** (phone that can activate, consent not `granted`, no current subscription): "Turn on
 *   notifications?" + Enable → {@link NotificationService.enable} (the real browser prompt); in the `denied`
 *   state it flips to "Notifications are blocked" + How-to-unblock → {@link DenyHelpComponent}.
 * - **desktop banner** (not a phone, nothing enabled anywhere): informational "Get notified on your phone",
 *   **no** activation CTA (desktop can't activate — see the Settings QR).
 * Both are dismissible for the session only. Refreshes the server-side device list on init so `anyEnabled`
 * is accurate for the desktop gate.
 */
@Component({
  selector: 'app-notif-banner',
  imports: [],
  templateUrl: './notif-banner.component.html',
  styleUrl: './notif-banner.component.scss',
})
export class NotifBannerComponent implements OnInit {
  private readonly notif = inject(NotificationService);
  private readonly dialog = inject(Dialog);

  /** Phone soft-ask: a push-capable phone, consent not yet granted, no current sub, not dismissed. */
  readonly showSoftAsk = computed(
    () =>
      this.notif.canActivate &&
      this.notif.permission() !== 'granted' &&
      !this.notif.hasCurrentSubscription() &&
      !this.notif.softAskDismissed(),
  );

  /** Within the soft-ask, whether to show the "blocked" copy (consent was denied) vs the normal prompt. */
  readonly softAskDenied = computed(() => this.notif.permission() === 'denied');

  /** Desktop informational banner: not a phone, nothing enabled anywhere, and not dismissed this session. */
  readonly showDeskBanner = computed(
    () => !this.notif.isMobile && !this.notif.anyEnabled() && !this.notif.softAskDismissed(),
  );

  ngOnInit(): void {
    // The device list (→ anyEnabled) is server-side; load it so the desktop banner gate is accurate.
    void this.notif.refreshDevices();
  }

  /** Enable CTA (soft-ask normal state) → request real permission + subscribe this phone. */
  enable(): void {
    void this.notif.enable();
  }

  /** How-to-unblock CTA (soft-ask denied state) → open the deny-help dialog. */
  openDenyHelp(): void {
    this.dialog.open(DenyHelpComponent);
  }

  /** Dismiss either banner for this session. */
  dismiss(): void {
    this.notif.dismissSoftAsk();
  }
}
