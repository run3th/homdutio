import { Component, OnInit, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { AuthService } from '../auth/auth.service';
import { HouseholdService } from '../household/household.service';
import { InviteService } from '../household/invite.service';

/** The preview lifecycle: still resolving the token, valid (household known), or invalid (404/410). */
type PreviewState = 'loading' | 'valid' | 'invalid';

/**
 * The public landing page for an invite link (S-06). One entry point handles every recipient state:
 *
 * - **Invalid token** (404 unknown / 410 consumed or expired) → "This invite is no longer valid."
 * - **Valid + logged out** → "Join [Household]" with Log in / Register actions that carry a `returnUrl`
 *   back to this page, so the token survives the auth hop (it lives only in the route param).
 * - **Valid + authenticated, no household** → a Join button that accepts the invite, caches membership,
 *   and lands on the board.
 * - **Valid + authenticated, already in a household** → blocked (FR-007), no join.
 *
 * The route carries no `requireHousehold`/`requireNoHousehold` guard — this component reads auth and
 * membership state itself and renders the right branch.
 */
@Component({
  selector: 'app-join',
  imports: [RouterLink],
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
  readonly joining = signal(false);
  /** A join-time error message (e.g. the invite was consumed between preview and accept). */
  readonly joinError = signal<string | null>(null);

  /** Public auth + membership state the template branches on. */
  readonly isAuthenticated = this.auth.isAuthenticated;
  readonly household = this.households.current;
  readonly membershipLoaded = this.households.loaded;

  ngOnInit(): void {
    this.token = this.route.snapshot.paramMap.get('token') ?? '';

    this.invites.preview(this.token).subscribe({
      next: (preview) => {
        this.householdName.set(preview.householdName);
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
}
