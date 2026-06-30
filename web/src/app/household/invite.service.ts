import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

import { Household } from './household.service';

/** Success body of `POST /api/households/invites` (camelCase JSON of the C# InviteResponse). */
export interface InviteResponse {
  token: string;
  expiresAtUtc: string;
}

/** Success body of `GET /api/households/invites/{token}` (camelCase JSON of InvitePreviewResponse). */
export interface InvitePreview {
  householdName: string;
  /** The inviter's display name, shown on the join screens. */
  inviterName: string;
  /** The inviter's user id — used to build their versioned avatar URL; shows their initial when they have no photo. */
  inviterId: string;
  /** The inviter's versioned avatar URL (S-09); null when they have no photo (render the initial). */
  inviterAvatarUrl?: string | null;
}

/**
 * Builds the shareable absolute URL a recipient opens from a raw invite token. The API returns only the
 * token (never a hard-coded host), so the SPA composes the link against its own origin — pure and
 * unit-testable, with no `window` coupling at call sites.
 */
export function buildJoinUrl(origin: string, token: string): string {
  return `${origin}/join/${token}`;
}

/**
 * Wraps the three invite endpoints (S-06): the board generates a link, the public join page previews the
 * household, and an authenticated recipient accepts. Preview/accept propagate their HTTP errors (404/410/409)
 * so the join page can branch on status; this service adds no error handling of its own.
 */
@Injectable({ providedIn: 'root' })
export class InviteService {
  private readonly http = inject(HttpClient);

  /**
   * `POST /api/households/invites` — mint a single-use link for the caller's household. When
   * `recipientEmail` is supplied the server also emails the `/join/<token>` link to that address; omit it
   * for the copy-link path. Either way the response is the freshly-minted token.
   */
  generate(recipientEmail?: string): Observable<InviteResponse> {
    const body = recipientEmail ? { recipientEmail } : {};
    return this.http.post<InviteResponse>('/api/households/invites', body);
  }

  /** `GET /api/households/invites/{token}` — public preview; 404 (unknown) / 410 (consumed/expired) surface as errors. */
  preview(token: string): Observable<InvitePreview> {
    return this.http.get<InvitePreview>(`/api/households/invites/${token}`);
  }

  /** `POST /api/households/invites/{token}/accept` — consume the invite and join; returns the new household. */
  accept(token: string): Observable<Household> {
    return this.http.post<Household>(`/api/households/invites/${token}/accept`, {});
  }
}
