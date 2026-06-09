import { Component, inject, signal } from '@angular/core';

import { HouseholdService } from '../../household/household.service';
import { buildJoinUrl, InviteService } from '../../household/invite.service';
import { AvatarMenuComponent } from './avatar-menu.component';

/**
 * The persistent top bar (S-11): the household name + role badge on the left, and on the right the
 * relocated **Invite a member** action and the avatar menu. Invite is lifted verbatim from the old
 * board header (S-06) — generate a single-use token, compose the `/join/<token>` URL against this
 * origin, best-effort copy it, and surface the link as selectable text so it works even where the
 * clipboard API is unavailable. (The **+ Add task** CTA joins this bar in Phase 3.)
 */
@Component({
  selector: 'app-topbar',
  imports: [AvatarMenuComponent],
  templateUrl: './topbar.component.html',
  styleUrl: './topbar.component.scss',
})
export class TopbarComponent {
  private readonly households = inject(HouseholdService);
  private readonly invites = inject(InviteService);

  readonly household = this.households.current;

  /** The generated `/join/<token>` URL to share, shown as selectable text (clipboard-copy fallback). */
  readonly inviteLink = signal<string | null>(null);
  /** Whether the link was copied to the clipboard (drives the "copied" confirmation). */
  readonly inviteCopied = signal(false);
  /** A generate-time error to surface inline. */
  readonly inviteError = signal<string | null>(null);
  readonly invitePending = signal(false);

  /**
   * Generate a single-use invite link and copy it to the clipboard to share out-of-band (FR-005).
   * The API returns only the token; the shareable URL is composed against this origin. The link is also
   * shown as selectable text so a recipient gets it even where `navigator.clipboard` is unavailable.
   */
  invite(): void {
    if (this.invitePending()) {
      return;
    }

    this.invitePending.set(true);
    this.inviteError.set(null);
    this.inviteCopied.set(false);

    this.invites.generate().subscribe({
      next: ({ token }) => {
        this.invitePending.set(false);
        const url = buildJoinUrl(window.location.origin, token);
        this.inviteLink.set(url);
        // Best-effort copy; the visible link is the fallback when the clipboard API is missing/denied.
        navigator.clipboard
          ?.writeText(url)
          .then(() => this.inviteCopied.set(true))
          .catch(() => this.inviteCopied.set(false));
      },
      error: () => {
        this.invitePending.set(false);
        this.inviteError.set('Could not create an invite. Please try again.');
      },
    });
  }
}
