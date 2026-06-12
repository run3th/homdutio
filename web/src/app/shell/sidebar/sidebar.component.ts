import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

/**
 * The persistent primary navigation (S-11). A light/translucent icon rail on desktop that the shell
 * reflows into a fixed bottom icon bar at ≤ 400px (NFR-2). Two destinations: Home (the board) and
 * Members (the S-09 roster + admin controls). A Settings item can drop in later without restructuring.
 */
@Component({
  selector: 'app-sidebar',
  imports: [RouterLink, RouterLinkActive],
  templateUrl: './sidebar.component.html',
  styleUrl: './sidebar.component.scss',
})
export class SidebarComponent {}
