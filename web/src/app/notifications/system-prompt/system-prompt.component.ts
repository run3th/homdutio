import { Component, inject } from '@angular/core';
import { Dialog, DialogRef } from '@angular/cdk/dialog';

import { NotificationService } from '../notification.service';
import { FlashService } from '../../shared/flash/flash.service';
import { DenyHelpComponent } from '../deny-help/deny-help.component';

/**
 * The *simulated* OS permission prompt (push-notifications) — a CDK dialog dressed up as a native
 * notification prompt. It touches no real permission API: **Allow** persists `granted` via
 * {@link NotificationService.grant} and shows a confirmation flash; **Don't Allow** persists `denied` via
 * {@link NotificationService.deny} and opens the {@link DenyHelpComponent} unblock instructions. Opened only
 * by {@link NotificationService.requestNotifs} (mobile-only, user-initiated). Closes with the resulting
 * permission so a caller can react.
 */
@Component({
  selector: 'app-system-prompt',
  imports: [],
  templateUrl: './system-prompt.component.html',
  styleUrl: './system-prompt.component.scss',
})
export class SystemPromptComponent {
  private readonly dialogRef = inject<DialogRef<'granted' | 'denied'>>(DialogRef);
  private readonly dialog = inject(Dialog);
  private readonly notif = inject(NotificationService);
  private readonly flash = inject(FlashService);

  allow(): void {
    this.notif.grant();
    this.flash.show('Notifications on for this device');
    this.dialogRef.close('granted');
  }

  deny(): void {
    this.notif.deny();
    this.dialogRef.close('denied');
    this.dialog.open(DenyHelpComponent);
  }
}
