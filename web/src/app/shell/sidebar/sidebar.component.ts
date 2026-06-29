import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { Dialog } from '@angular/cdk/dialog';

import { CreateTaskComponent } from '../../board/create-task/create-task.component';
import { TaskService } from '../../board/task.service';

/**
 * The persistent primary navigation (S-11), redesigned to the mockup: a 176px text-label rail with a
 * status dot per destination on desktop, reflowing into a fixed bottom nav with a center FAB at the
 * mobile breakpoint (< 1000px; NFR-2 holds at ≤ 400px, a subset). Destinations: Board and Members.
 * The bottom-nav FAB opens the create-task dialog (pausing board polling while open) — mirroring the
 * topbar's "+ New task" CTA — so "new task" stays one tap away when the header buttons are hidden.
 */
@Component({
  selector: 'app-sidebar',
  imports: [RouterLink, RouterLinkActive],
  templateUrl: './sidebar.component.html',
  styleUrl: './sidebar.component.scss',
})
export class SidebarComponent {
  private readonly dialog = inject(Dialog);
  private readonly tasks = inject(TaskService);

  addTask(): void {
    this.tasks.setPaused(true);
    const ref = this.dialog.open(CreateTaskComponent);
    ref.closed.subscribe(() => this.tasks.setPaused(false));
  }
}
