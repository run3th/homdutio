import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

/** A household role, mirroring the C# HouseholdRole enum (persisted as a string). */
export type MemberRole = 'Admin' | 'Member';

/**
 * One roster row, mirroring the camelCase JSON of the C# MemberResponse (S-09). The server computes
 * {@link isSelf} (this row is the caller) and {@link canManage} (the caller may promote/demote/remove
 * this row — admin, and not themselves), so the page renders controls from flags rather than re-deriving
 * authorization — the same dumb-client contract the task affordance flags use.
 */
export interface Member {
  userId: string;
  displayName: string;
  email: string;
  role: MemberRole;
  isSelf: boolean;
  canManage: boolean;
  /** The member's versioned avatar URL (S-09); null when they have no photo (render the initial). */
  avatarUrl?: string | null;
}

/**
 * Wraps the three S-09 member-administration endpoints (FR-008/FR-009). All are admin-gated, self-action-
 * blocked, and last-admin-guarded server-side; this service adds no rules of its own and lets HTTP errors
 * (403/404/409) propagate so the page can surface the server's message inline.
 */
@Injectable({ providedIn: 'root' })
export class MemberService {
  private readonly http = inject(HttpClient);

  /** `GET /api/households/members` — the caller's household roster. */
  list(): Observable<Member[]> {
    return this.http.get<Member[]>('/api/households/members');
  }

  /** `POST /api/households/members/{userId}/role` — promote/demote; returns the updated row. */
  setRole(userId: string, role: MemberRole): Observable<Member> {
    return this.http.post<Member>(`/api/households/members/${userId}/role`, { role });
  }

  /** `DELETE /api/households/members/{userId}` — remove a member (server sweeps their in-progress tasks). */
  remove(userId: string): Observable<void> {
    return this.http.delete<void>(`/api/households/members/${userId}`);
  }
}
