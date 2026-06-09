import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

import { SidebarComponent } from './sidebar/sidebar.component';
import { TopbarComponent } from './topbar/topbar.component';

/**
 * The persistent authenticated layout frame (S-11): a sidebar, a topbar, and a `<router-outlet>` for
 * the page content. Wraps only the household routes (the auth/join pages stay full-page). Owns the
 * desktop-rail ↔ mobile-bottom-bar responsive switch via CSS — no JS, so there's no layout shift on
 * resize. No business logic lives here.
 */
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, SidebarComponent, TopbarComponent],
  templateUrl: './shell.component.html',
  styleUrl: './shell.component.scss',
})
export class ShellComponent {}
