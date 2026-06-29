import { Component, inject, signal } from '@angular/core';
import { DialogRef } from '@angular/cdk/dialog';

import { HouseholdService } from '../../household/household.service';
import { buildJoinUrl, InviteService } from '../../household/invite.service';

/**
 * The invite modal (S-06), opened from the topbar's **Invite** button via `@angular/cdk/dialog`. On open it
 * mints a single-use token and composes the `/join/<token>` URL against this origin, surfacing it in a
 * selectable mono link box with a **Copy** button — the visible link is the fallback wherever
 * `navigator.clipboard` is unavailable or denied. The API returns only the token (never a hard-coded host),
 * so the shareable URL is built client-side via {@link buildJoinUrl}.
 *
 * The markup leaves a slot above the share-link box for the optional "Invite by email" field added in a
 * later phase; for now the dialog is copy-link only.
 */
@Component({
  selector: 'app-invite-dialog',
  imports: [],
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

  close(): void {
    this.dialogRef.close();
  }
}
