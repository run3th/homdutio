import { Component, inject, signal } from '@angular/core';
import { DialogRef } from '@angular/cdk/dialog';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';

import { HouseholdService } from '../../household/household.service';
import { buildJoinUrl, InviteService } from '../../household/invite.service';

/**
 * The invite modal (S-06), opened from the topbar's **Invite** button via `@angular/cdk/dialog`. On open it
 * mints a single-use token and composes the `/join/<token>` URL against this origin, surfacing it in a
 * selectable mono link box with a **Copy** button — the visible link is the fallback wherever
 * `navigator.clipboard` is unavailable or denied. The API returns only the token (never a hard-coded host),
 * so the shareable URL is built client-side via {@link buildJoinUrl}.
 *
 * Above the share-link box an optional **Invite by email** field sends the link directly: it mints a fresh
 * invite server-side (each is a valid single-use token) and the server emails the `/join/<token>` link to
 * that address. The copy-link path stays fully independent of whether an email was sent.
 */
@Component({
  selector: 'app-invite-dialog',
  imports: [ReactiveFormsModule],
  templateUrl: './invite-dialog.component.html',
  styleUrl: './invite-dialog.component.scss',
})
export class InviteDialogComponent {
  private readonly invites = inject(InviteService);
  private readonly households = inject(HouseholdService);
  private readonly dialogRef = inject<DialogRef<void>>(DialogRef);

  readonly household = this.households.current;

  /** The generated `/join/<token>` URL to share, shown as selectable text (clipboard-copy fallback). */
  readonly inviteLink = signal<string | null>(null);
  /** Whether the link was copied to the clipboard (drives the "Copied" confirmation). */
  readonly inviteCopied = signal(false);
  /** A generate-time error to surface inline. */
  readonly inviteError = signal<string | null>(null);
  /** True while the token is being minted (the link box shows a placeholder until it resolves). */
  readonly generating = signal(true);

  /** Optional recipient address; on submit the server mints a fresh invite and emails the link to it. */
  readonly email = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.email],
  });
  /** True while an invite email is being sent (disables Send). */
  readonly sending = signal(false);
  /** The address the most recent invite was emailed to (drives the "Invite sent" confirmation). */
  readonly sentTo = signal<string | null>(null);
  /** A send-time error to surface inline. */
  readonly sendError = signal<string | null>(null);

  constructor() {
    this.invites.generate().subscribe({
      next: ({ token }) => {
        this.generating.set(false);
        this.inviteLink.set(buildJoinUrl(window.location.origin, token));
      },
      error: () => {
        this.generating.set(false);
        this.inviteError.set('Could not create an invite. Please try again.');
      },
    });
  }

  /** Best-effort copy of the generated link; the visible link is the fallback when the clipboard is missing. */
  copy(): void {
    const link = this.inviteLink();
    if (!link) {
      return;
    }
    navigator.clipboard
      ?.writeText(link)
      .then(() => this.inviteCopied.set(true))
      .catch(() => this.inviteCopied.set(false));
  }

  /** Mint a fresh invite and have the server email it to the entered address; confirm or surface an error. */
  sendEmail(): void {
    if (this.sending() || this.email.invalid) {
      this.email.markAsTouched();
      return;
    }
    const recipient = this.email.value.trim();
    this.sending.set(true);
    this.sendError.set(null);
    this.invites.generate(recipient).subscribe({
      next: () => {
        this.sending.set(false);
        this.sentTo.set(recipient);
        this.email.reset('');
      },
      error: () => {
        this.sending.set(false);
        this.sendError.set('Could not send the invite. Please try again.');
      },
    });
  }

  close(): void {
    this.dialogRef.close();
  }
}
