import { Component, inject } from '@angular/core';
import { DialogRef } from '@angular/cdk/dialog';

/**
 * Post-denial help (push-notifications) — a static CDK dialog explaining how to re-enable notifications
 * in the browser after the simulated prompt was declined. Opened from the soft-ask's denied-state CTA
 * ("How to unblock") and from the system prompt's "Don't Allow". Purely instructional: it changes no state
 * and closes on the single "Got it" button.
 */
@Component({
  selector: 'app-deny-help',
  imports: [],
  templateUrl: './deny-help.component.html',
  styleUrl: './deny-help.component.scss',
})
export class DenyHelpComponent {
  private readonly dialogRef = inject<DialogRef<void>>(DialogRef);

  close(): void {
    this.dialogRef.close();
  }
}
