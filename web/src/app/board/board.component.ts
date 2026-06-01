import { Component, inject } from '@angular/core';

import { HouseholdService } from '../household/household.service';

/**
 * The household board (S-02). Renders the household identity (name + role badge) and three empty,
 * responsive columns. There is no task data yet — the columns are static markup whose layout (mobile
 * stack → side-by-side above a breakpoint, no horizontal scroll at ≤ 400px per NFR-2) is inherited by
 * every later board slice.
 */
@Component({
  selector: 'app-board',
  imports: [],
  templateUrl: './board.component.html',
  styleUrl: './board.component.scss',
})
export class BoardComponent {
  private readonly households = inject(HouseholdService);

  readonly household = this.households.current;

  /** The three fixed board columns (English labels; no i18n in v1). */
  readonly columns = ['To do', 'In progress', 'Done'] as const;
}
