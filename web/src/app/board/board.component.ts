import { Component, inject } from '@angular/core';

import { HouseholdService } from '../household/household.service';

/**
 * The household board. Phase 2 ships a minimal placeholder so the membership guard has a redirect
 * target and the create flow is verifiable end-to-end; Phase 3 replaces the template with the real
 * header (name + role badge) and the three responsive columns.
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
}
