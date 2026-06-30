import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';

import { AuthService } from '../auth/auth.service';

/** Success body of `PUT /api/profile/me` (camelCase JSON of the C# ProfileResponse). */
export interface ProfileResponse {
  id: string;
  displayName: string;
  avatarUrl: string | null;
}

/**
 * Self-service profile mutations (S-09). Changing the display name propagates everywhere via the
 * backend's fetch-time name resolution, so the board's next refresh shows the new name on existing
 * cards/comments; here we also push the change into {@link AuthService} so the header/menu update
 * immediately. Avatar upload/remove arrives in Phase 3.
 */
@Injectable({ providedIn: 'root' })
export class ProfileService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);

  /**
   * `PUT /api/profile/me` — change the signed-in user's display name. On success updates the current-user
   * display-name signal so the header/menu reflect it without a re-fetch; 400 ValidationProblem on a
   * blank/too-long name.
   */
  updateProfile(displayName: string): Observable<ProfileResponse> {
    return this.http.put<ProfileResponse>('/api/profile/me', { displayName }).pipe(
      tap((profile) =>
        this.auth.setProfile({ displayName: profile.displayName, avatarUrl: profile.avatarUrl }),
      ),
    );
  }
}
