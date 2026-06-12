import { Component, inject } from '@angular/core';
import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';

import { Member } from '../member.service';

/**
 * A small, dedicated confirm dialog for the destructive **remove member** action (S-09), opened by the
 * Members page so eviction never happens on a single mis-click. Closes with `true` on confirm and
 * `false`/undefined on cancel/backdrop; the page performs the actual {@link MemberService.remove} + refetch
 * on a `true` result. A sibling of (not a reuse of) the board's task delete-confirm, which is typed to a
 * Task — this one carries a {@link Member} and its own copy. The server guards (admin/self/last-admin)
 * remain authoritative, so the page self-heals if the affordance was stale.
 */
@Component({
  selector: 'app-remove-member-confirm',
  imports: [],
  templateUrl: './remove-member-confirm.component.html',
  styleUrl: './remove-member-confirm.component.scss',
})
export class RemoveMemberConfirmComponent {
  private readonly dialogRef = inject<DialogRef<boolean>>(DialogRef);

  /** The member to remove, handed in by the page via CDK `DIALOG_DATA` (their name is shown in the prompt). */
  readonly member = inject<Member>(DIALOG_DATA);

  confirm(): void {
    this.dialogRef.close(true);
  }

  cancel(): void {
    this.dialogRef.close(false);
  }
}
