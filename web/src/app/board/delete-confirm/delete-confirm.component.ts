import { Component, inject } from '@angular/core';
import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';

import { Task } from '../task.service';

/**
 * A small, dedicated delete-confirm dialog (S-11), opened by the board from a card's `delete` event so
 * destruction never sits beside Save in the edit form. Closes with `true` on confirm and `false`/undefined
 * on cancel/backdrop; the board performs the actual {@link TaskService.delete} + refetch on a `true` result.
 * The To-do-only + 409 guards remain server-side, so the board self-heals if the affordance was stale.
 */
@Component({
  selector: 'app-delete-confirm',
  imports: [],
  templateUrl: './delete-confirm.component.html',
  styleUrl: './delete-confirm.component.scss',
})
export class DeleteConfirmComponent {
  private readonly dialogRef = inject<DialogRef<boolean>>(DialogRef);

  /** The task to delete, handed in by the board via CDK `DIALOG_DATA` (its title is shown in the prompt). */
  readonly task = inject<Task>(DIALOG_DATA);

  confirm(): void {
    this.dialogRef.close(true);
  }

  cancel(): void {
    this.dialogRef.close(false);
  }
}
