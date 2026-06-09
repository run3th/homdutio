import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

/**
 * The persistent primary navigation (S-11). A light/translucent icon rail on desktop that the shell
 * reflows into a fixed bottom icon bar at ≤ 400px (NFR-2). Shows only the destinations that exist
 * today — Home and Tasks, both pointing at the single board — so a Members/Settings item (S-09) drops
 * in later without restructuring. They share `/board` for now (documented interim) and diverge once a
 * distinct home/members page lands.
 */
@Component({
  selector: 'app-sidebar',
  imports: [RouterLink, RouterLinkActive],
  templateUrl: './sidebar.component.html',
  styleUrl: './sidebar.component.scss',
})
export class SidebarComponent {}
