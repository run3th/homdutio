import { Component, inject } from '@angular/core';
import { Router } from '@angular/router';

import { AuthService } from '../auth/auth.service';

/**
 * Placeholder authenticated landing page. Proves the guard + token end-to-end and hosts
 * logout. Deliberately disposable — S-02 (household/board) replaces it.
 */
@Component({
  selector: 'app-home',
  imports: [],
  templateUrl: './home.component.html',
  styleUrl: './home.component.scss',
})
export class HomeComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly email = this.auth.email;

  logout(): void {
    this.auth.logout();
    void this.router.navigate(['/login']);
  }
}
