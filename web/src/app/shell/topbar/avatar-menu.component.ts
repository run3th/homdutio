import { Component, computed, ElementRef, HostListener, inject, signal } from '@angular/core';
import { Router } from '@angular/router';

import { AuthService } from '../../auth/auth.service';

/**
 * The avatar button + dropdown that finally surfaces logout (S-11) and is the future home for S-09
 * account/settings items. Shows the signed-in email and a Log out action; logout clears auth state and
 * navigates to `/login` — the same path the unauthorized interceptor already redirects to. The menu is
 * single-open and closes on outside-click or Escape (`aria-expanded` reflects state).
 */
@Component({
  selector: 'app-avatar-menu',
  imports: [],
  templateUrl: './avatar-menu.component.html',
  styleUrl: './avatar-menu.component.scss',
})
export class AvatarMenuComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly host = inject(ElementRef<HTMLElement>);

  /** The signed-in user's email, shown in the menu. */
  readonly email = this.auth.email;
  /** Whether the dropdown is open. */
  readonly open = signal(false);

  /** First letter of the email for the avatar glyph; a neutral placeholder before login resolves. */
  readonly initial = computed(() => {
    const email = this.email();
    return email ? email.charAt(0).toUpperCase() : '?';
  });

  toggle(): void {
    this.open.update((isOpen) => !isOpen);
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
