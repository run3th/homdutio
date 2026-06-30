import { Component, inject, OnInit, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { Dialog } from '@angular/cdk/dialog';

import { Member, MemberRole, MemberService } from '../member.service';
import { RemoveMemberConfirmComponent } from './remove-member-confirm.component';
import { UserAvatarComponent } from '../../shared/user-avatar/user-avatar.component';

/**
 * The household members page (S-09). Lists the roster with each member's name, email, and role. For rows
 * the server marks {@link Member.canManage} (the caller is an admin and it isn't their own row), it offers
 * promote/demote — applied immediately, since a role flip is reversible — and a Remove that goes through a
 * confirm dialog, since eviction is not. A non-admin sees the same roster with no controls (read-only),
 * because every row comes back with `canManage = false`. The roster is fetched on load and refetched after
 * the caller's own action (no polling — member admin is rare and off the live-board path). Server guards
 * (admin/self/last-admin) are authoritative; their 403/409 messages are surfaced inline.
 */
@Component({
  selector: 'app-members',
  imports: [UserAvatarComponent],
  templateUrl: './members.component.html',
  styleUrl: './members.component.scss',
})
export class MembersComponent implements OnInit {
  private readonly members = inject(MemberService);
  private readonly dialog = inject(Dialog);

  /** The current roster. */
  readonly roster = signal<Member[]>([]);
  /** True only during the initial load (drives the loading line). */
  readonly loading = signal(true);
  /** A surfaced error (load failure or a rejected action's server message). */
  readonly error = signal<string | null>(null);
  /** The userId of the row currently mutating — disables its buttons to block double-submits. */
  readonly pending = signal<string | null>(null);

  ngOnInit(): void {
    this.load();
  }

  /** Promote a member to admin (FR-008) — immediate, then refetch. */
  promote(member: Member): void {
    this.changeRole(member, 'Admin');
  }

  /** Demote an admin back to member (FR-008) — immediate, then refetch. */
  demote(member: Member): void {
    this.changeRole(member, 'Member');
  }

  /**
   * Remove a member (FR-009) behind a confirm dialog; on confirm, calls {@link MemberService.remove} and
   * refetches the roster so the removed row disappears. A rejected action surfaces the server's message.
   */
  remove(member: Member): void {
    if (this.pending()) {
      return;
    }

    const ref = this.dialog.open<boolean>(RemoveMemberConfirmComponent, { data: member });
    ref.closed.subscribe((confirmed) => {
      if (!confirmed) {
        return;
      }

      this.pending.set(member.userId);
      this.error.set(null);
      this.members.remove(member.userId).subscribe({
        next: () => {
          this.pending.set(null);
          this.load();
        },
        error: (err: HttpErrorResponse) => {
          this.pending.set(null);
          this.error.set(messageFrom(err));
        },
      });
    });
  }

  private changeRole(member: Member, role: MemberRole): void {
    if (this.pending()) {
      return;
    }

    this.pending.set(member.userId);
    this.error.set(null);
    this.members.setRole(member.userId, role).subscribe({
      next: () => {
        this.pending.set(null);
        this.load();
      },
      error: (err: HttpErrorResponse) => {
        this.pending.set(null);
        this.error.set(messageFrom(err));
      },
    });
  }

  private load(): void {
    this.members.list().subscribe({
      next: (roster) => {
        this.roster.set(roster);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load members. Please try again.');
        this.loading.set(false);
      },
    });
  }
}

/** Prefers the server's `{ message }` (the 409 guards), falling back to status-specific copy. */
function messageFrom(error: HttpErrorResponse): string {
  const message = (error.error as { message?: string } | null)?.message;
  if (message) {
    return message;
  }

  return error.status === 403
    ? 'Only an admin can manage members.'
    : 'Something went wrong. Please try again.';
}
