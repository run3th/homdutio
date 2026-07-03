import { Component, computed, inject } from '@angular/core';
import { Dialog } from '@angular/cdk/dialog';

import { NotificationService } from '../notification.service';
import { DenyHelpComponent } from '../deny-help/deny-help.component';

/**
 * The Board's notification banner (push-notifications), sitting above the columns. Its visibility mirrors
 * the reference's per-device rules, all derived from {@link NotificationService}:
 * - **soft-ask** (mobile, consent not `granted`): "Turn on notifications?" + Enable → {@link NotificationService.requestNotifs};
 *   in the `denied` state it flips to "Notifications are blocked" + How-to-unblock → {@link DenyHelpComponent}.
 * - **desktop banner** (not mobile, nothing enabled): informational "Get notified on your phone", **no**
 *   activation CTA (desktop never activates).
 * Both are dismissible for the session only ({@link NotificationService.dismissSoftAsk}). Renders nothing
 * when neither gate is open.
 */
@Component({
  selector: 'app-notif-banner',
  imports: [],
  templateUrl: './notif-banner.component.html',
  styleUrl: './notif-banner.component.scss',
})
export class NotifBannerComponent {
  private readonly notif = inject(NotificationService);
  private readonly dialog = inject(Dialog);

  /** Mobile soft-ask: phone, consent not yet granted, and not dismissed this session. */
  readonly showSoftAsk = computed(
    () =>
      this.notif.isMobile() &&
      this.notif.permission() !== 'granted' &&
      !this.notif.softAskDismissed(),
  );

  /** Within the soft-ask, whether to show the "blocked" copy (consent was denied) vs the normal prompt. */
  readonly softAskDenied = computed(() => this.notif.permission() === 'denied');

  /** Desktop informational banner: not a phone, nothing enabled anywhere, and not dismissed this session. */
  readonly showDeskBanner = computed(
    () => !this.notif.isMobile() && !this.notif.anyEnabled() && !this.notif.softAskDismissed(),
  );

  /** Enable CTA (soft-ask normal state) → open the simulated OS prompt. */
  enable(): void {
    this.notif.requestNotifs();
  }

  /** How-to-unblock CTA (soft-ask denied state) → open the static deny-help dialog. */
  openDenyHelp(): void {
    this.dialog.open(DenyHelpComponent);
  }

  /** Dismiss either banner for this session. */
  dismiss(): void {
    this.notif.dismissSoftAsk();
  }
}
