import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { AuthService } from '../auth/auth.service';
import { HouseholdService } from '../household/household.service';
import { InviteService } from '../household/invite.service';
import { AuthLogoComponent } from '../auth/auth-logo.component';
import { UserAvatarComponent } from '../shared/user-avatar/user-avatar.component';

/** The preview lifecycle: still resolving the token, valid (household known), or invalid (404/410). */
type PreviewState = 'loading' | 'valid' | 'invalid';

/**
 * The screen the template renders, derived from preview + auth + membership state:
 * - `loading` — still resolving the preview, or (when authenticated) the membership check.
 * - `invalid` — the token is unknown/consumed/expired (404/410).
 * - `joinLoggedOut` — valid token, not signed in: a branded landing naming the inviter + household.
 * - `join` — valid token, signed in, no household yet: the accept-the-invite screen.
 * - `joinTaken` — valid token, signed in, already in a household: a calm "you're already in" (FR-007).
 */
type Screen = 'loading' | 'invalid' | 'joinLoggedOut' | 'join' | 'joinTaken';

/**
 * The public landing page for an invite link (S-06). One entry point handles every recipient state and
 * renders one of the designed screens (see {@link Screen}). The route carries no
 * `requireHousehold`/`requireNoHousehold` guard — this component reads auth + membership state itself and
 * picks the screen, so a logged-out recipient can preview, then log in / register (carrying a `returnUrl`
 * back here, since the token lives only in the route param) and return to the right branch.
 */
@Component({
  selector: 'app-join',
  imports: [RouterLink, AuthLogoComponent, UserAvatarComponent],
  templateUrl: './join.component.html',
  styleUrl: './join.component.scss',
})
export class JoinComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly invites = inject(InviteService);
  private readonly auth = inject(AuthService);
  private readonly households = inject(HouseholdService);

  /** The invite token from the route; the `/login?returnUrl=` target is built from it. */
  private token = '';

  readonly previewState = signal<PreviewState>('loading');
  readonly householdName = signal<string | null>(null);
  /** The inviter named on the join screens; resolved from the public preview. */
  readonly inviterName = signal<string | null>(null);
  /** The inviter's avatar URL from the preview; null when they have no photo (the badge shows their initial). */
  readonly inviterAvatarUrl = signal<string | null>(null);
  readonly joining = signal(false);
  /** A join-time error message (e.g. the invite was consumed between preview and accept). */
  readonly joinError = signal<string | null>(null);

  /** Public auth + membership state the screen selection branches on. */
  readonly isAuthenticated = this.auth.isAuthenticated;
  readonly household = this.households.current;
  readonly membershipLoaded = this.households.loaded;

  /** The single screen to render, derived from preview + auth + membership (see {@link Screen}). */
  readonly screen = computed<Screen>(() => {
    const preview = this.previewState();
    if (preview === 'loading') {
      return 'loading';
    }
    if (preview === 'invalid') {
      return 'invalid';
    }
    if (!this.isAuthenticated()) {
      return 'joinLoggedOut';
    }
    // Authenticated: wait for membership to resolve before choosing join vs. already-in-household,
    // so the user never clicks into a 409 (the joinTaken decision happens on load, not on click).
    if (!this.membershipLoaded()) {
      return 'loading';
    }
    return this.household() ? 'joinTaken' : 'join';
  });

  ngOnInit(): void {
    this.token = this.route.snapshot.paramMap.get('token') ?? '';

    this.invites.preview(this.token).subscribe({
      next: (preview) => {
        this.householdName.set(preview.householdName);
        this.inviterName.set(preview.inviterName);
        this.inviterAvatarUrl.set(preview.inviterAvatarUrl ?? null);
        this.previewState.set('valid');
        // Resolve membership so the authenticated branch knows join vs. already-in-household.
        if (this.isAuthenticated()) {
          this.households.loadMine().subscribe();
        }
      },
      error: () => this.previewState.set('invalid'),
    });
  }

  /** The `/login` (and `/register`) returnUrl that brings the recipient back here after authenticating. */
  get returnUrl(): string {
    return `/join/${this.token}`;
  }

  /** Accept the invite, cache the joined household, and land on the board. */
  join(): void {
    if (this.joining()) {
      return;
    }

    this.joining.set(true);
    this.joinError.set(null);

    this.invites.accept(this.token).subscribe({
      next: (household) => {
        this.households.setMembership(household);
        void this.router.navigate(['/board']);
      },
      error: (error: HttpErrorResponse) => {
        this.joining.set(false);
        if (error.status === 410) {
          // Consumed/expired between preview and accept — fall back to the invalid screen.
          this.previewState.set('invalid');
        } else if (error.status === 409) {
          // Already in a household — surface the block by refreshing membership state.
          this.joinError.set('You already belong to a household.');
        } else {
          this.joinError.set('Something went wrong. Please try again.');
        }
      },
    });
  }

  /** "No thanks, maybe later" — leave the invite and head into the app (the guard routes from here). */
  decline(): void {
    void this.router.navigate(['/board']);
  }
}
