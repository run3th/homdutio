import { Component, inject } from '@angular/core';
import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { ReactiveFormsModule, FormControl, Validators } from '@angular/forms';

import { Task } from '../task.service';

/**
 * A small, dedicated send-back dialog (S-05), opened by the board from a card's `sendBack` event. An admin
 * returns a Done task to In progress with a **required** short reason; that reason is posted atomically as a
 * SendBack-kind comment by the server. Closes with the trimmed comment on confirm and `undefined` on
 * cancel/backdrop; the board performs the actual {@link TaskService.sendBack} + refetch on a returned comment.
 * The not-admin / not-Done guards remain server-side, so the board self-heals if the affordance was stale.
 */
@Component({
  selector: 'app-send-back',
  imports: [ReactiveFormsModule],
  templateUrl: './send-back.component.html',
  styleUrl: './send-back.component.scss',
})
export class SendBackComponent {
  private readonly dialogRef = inject<DialogRef<string | undefined>>(DialogRef);

  /** The task to send back, handed in by the board via CDK `DIALOG_DATA` (its title is shown in the prompt). */
  readonly task = inject<Task>(DIALOG_DATA);

  /** The required reason; bounded to the server's 280-char limit so the UI rejects before the round-trip. */
  readonly comment = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.maxLength(280)],
  });

  submit(): void {
    const value = this.comment.value.trim();
    if (!value) {
      this.comment.markAsTouched();
      return;
    }

    this.dialogRef.close(value);
  }

  cancel(): void {
    this.dialogRef.close(undefined);
  }
}
