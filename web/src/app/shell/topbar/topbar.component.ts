import { Component, inject } from '@angular/core';
import { Dialog } from '@angular/cdk/dialog';

import { HouseholdService } from '../../household/household.service';
import { CreateTaskComponent } from '../../board/create-task/create-task.component';
import { TaskService } from '../../board/task.service';
import { AvatarMenuComponent } from './avatar-menu.component';
import { InviteDialogComponent } from './invite-dialog.component';

/**
 * The persistent top bar (S-11): the logo lockup + workspace pill (household name + role badge) on the left,
 * and on the right the **Invite** action, the **＋ New task** CTA, and the avatar menu. **Invite** opens the
 * invite modal ({@link InviteDialogComponent}), which mints the `/join/<token>` link and offers copy-to-share.
 * **＋ New task** opens the create-task dialog and pauses board polling while it's open (mirroring the detail
 * dialog) so a 4s tick never refetches mid-entry; the shared root-provided {@link TaskService} carries both
 * the pause flag and the refetch the dialog triggers.
 */
@Component({
  selector: 'app-topbar',
  imports: [AvatarMenuComponent],
  templateUrl: './topbar.component.html',
  styleUrl: './topbar.component.scss',
})
export class TopbarComponent {
  private readonly households = inject(HouseholdService);
  private readonly dialog = inject(Dialog);
  private readonly tasks = inject(TaskService);

  readonly household = this.households.current;

  /**
   * Open the create-task dialog (FR-010) from the topbar CTA. Polling is paused while it's open and resumed
   * on close — mirroring the detail dialog — so a 4s tick never refetches the board mid-entry (F-03). The
   * dialog's own success refetch (via {@link TaskService.create}) updates the shared board state even while
   * paused, so the new "To do" card appears the moment the dialog closes.
   */
  addTask(): void {
    this.tasks.setPaused(true);
    const ref = this.dialog.open(CreateTaskComponent);
    ref.closed.subscribe(() => this.tasks.setPaused(false));
  }

  /** Open the invite modal (FR-005); it mints a single-use `/join/<token>` link and offers copy-to-share. */
  invite(): void {
    this.dialog.open(InviteDialogComponent);
  }
}
