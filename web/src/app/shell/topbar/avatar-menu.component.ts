import { Component, ElementRef, HostListener, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Dialog } from '@angular/cdk/dialog';

import { AuthService } from '../../auth/auth.service';
import { UserAvatarComponent } from '../../shared/user-avatar/user-avatar.component';
import { SettingsDialogComponent } from '../settings-dialog/settings-dialog.component';

/**
 * The avatar button + dropdown that surfaces the signed-in identity, **Settings** (S-09), and **Log out**
 * (S-11). The button shows the user's avatar (their photo once Phase 3 lands, otherwise the colored
 * initial); the open menu shows the display name as the primary identity with the email demoted to a
 * secondary line, then a Settings item above Log out. Settings opens the {@link SettingsDialogComponent}
 * (edit display name; photo upload/remove in Phase 3). Logout clears auth state and navigates to `/login`.
 * The menu is single-open and closes on outside-click or Escape (`aria-expanded` reflects state).
 */
@Component({
  selector: 'app-avatar-menu',
  imports: [UserAvatarComponent],
  templateUrl: './avatar-menu.component.html',
  styleUrl: './avatar-menu.component.scss',
})
export class AvatarMenuComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly dialog = inject(Dialog);
  private readonly host = inject(ElementRef<HTMLElement>);

  /** The signed-in user's display name (primary identity); `null` until `/me` resolves. */
  readonly displayName = this.auth.displayName;
  /** The signed-in user's email, shown as a secondary line under the name. */
  readonly email = this.auth.email;
  /** The signed-in user's avatar URL, or `null` to fall back to the colored initial. */
  readonly avatarUrl = this.auth.avatarUrl;
  /** Whether the dropdown is open. */
  readonly open = signal(false);

  toggle(): void {
    this.open.update((isOpen) => !isOpen);
  }

  /** Open the Settings modal (S-09) to edit the display name (and, from Phase 3, the profile photo). */
  openSettings(): void {
    this.open.set(false);
    this.dialog.open(SettingsDialogComponent);
  }

  /** Clear auth state (revokes the session, drops tokens + household/task state) then land on /login. */
  logout(): void {
    this.open.set(false);
    this.auth.logout();
    void this.router.navigate(['/login']);
  }

  /** Close on a click anywhere outside the menu (the button's own click toggles before this fires). */
  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (this.open() && !this.host.nativeElement.contains(event.target as Node)) {
      this.open.set(false);
    }
  }

  /** Close on Escape so keyboard users aren't trapped in the open menu. */
  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.open()) {
      this.open.set(false);
    }
  }
}
