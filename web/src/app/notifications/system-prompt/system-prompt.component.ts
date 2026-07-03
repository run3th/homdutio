import { Component, inject } from '@angular/core';
import { DialogRef } from '@angular/cdk/dialog';

import { NotificationService } from '../notification.service';
import { FlashService } from '../../shared/flash/flash.service';

/**
 * The *simulated* OS permission prompt (push-notifications) — a CDK dialog dressed up as a native
 * notification prompt. It touches no real permission API: **Allow** persists `granted` via
 * {@link NotificationService.grant} and shows a confirmation flash; **Don't Allow** persists `denied` via
 * {@link NotificationService.deny}. Opened only by {@link NotificationService.requestNotifs} (mobile-only,
 * user-initiated). Closes with the resulting permission so a caller can react (the deny-help panel is Phase 3).
 */
@Component({
  selector: 'app-system-prompt',
  imports: [],
  templateUrl: './system-prompt.component.html',
  styleUrl: './system-prompt.component.scss',
})
export class SystemPromptComponent {
  private readonly dialogRef = inject<DialogRef<'granted' | 'denied'>>(DialogRef);
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
  }
}
