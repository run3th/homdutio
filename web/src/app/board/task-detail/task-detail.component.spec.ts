import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { of, throwError } from 'rxjs';

import { TaskDetailComponent } from './task-detail.component';
import { Task, TaskService } from '../task.service';
import { Member, MemberService } from '../../household/member.service';
import { FlashService } from '../../shared/flash/flash.service';

describe('TaskDetailComponent', () => {
  let update: ReturnType<typeof vi.fn>;
  let assign: ReturnType<typeof vi.fn>;
  let deleteTask: ReturnType<typeof vi.fn>;
  let getComments: ReturnType<typeof vi.fn>;
  let addComment: ReturnType<typeof vi.fn>;
  let list: ReturnType<typeof vi.fn>;
  let show: ReturnType<typeof vi.fn>;
  let close: ReturnType<typeof vi.fn>;

  const roster: Member[] = [
    { userId: 'me', displayName: 'Rafał', email: 'r@x', role: 'Admin', isSelf: true, canManage: false },
    { userId: 'u2', displayName: 'Molly', email: 'm@x', role: 'Member', isSelf: false, canManage: true },
  ];

  function baseTask(overrides: Partial<Task>): Task {
    return {
      id: 't1',
      title: 'Take out bins',
      description: null,
      tags: [],
      status: 'ToDo',
      createdByName: 'Molly',
      claimerName: null,
      createdAtUtc: '2026-06-01T10:00:00Z',
      canClaim: false,
      canAssign: false,
      canMarkDone: false,
      canConfirm: false,
      willSelfAttest: false,
      canEdit: true,
      canDelete: true,
      canUnclaim: false,
      canSendBack: false,
      commentCount: 0,
      ...overrides,
    };
  }

  function configure(task: Task, updateImpl?: ReturnType<typeof vi.fn>) {
    // Reset so a single test may render twice with different task flags (the picker-visibility test does).
    TestBed.resetTestingModule();
    update = updateImpl ?? vi.fn(() => of([]));
    assign = vi.fn(() => of([]));
    deleteTask = vi.fn(() => of([]));
    getComments = vi.fn(() => of([]));
    addComment = vi.fn(() => of({}));
    list = vi.fn(() => of(roster));
    show = vi.fn();
    close = vi.fn();
    TestBed.configureTestingModule({
      imports: [TaskDetailComponent],
      providers: [
        { provide: DIALOG_DATA, useValue: task },
        { provide: DialogRef, useValue: { close } },
        {
          provide: TaskService,
          useValue: { update, assign, delete: deleteTask, getComments, addComment, getTagSuggestions: () => of([]) },
        },
        { provide: MemberService, useValue: { list } },
        { provide: FlashService, useValue: { show } },
      ],
    });
  }

  // `deleteTask` is provided only to satisfy the injector; the edit dialog never calls it (delete moved to
  // the card's ⋯ menu, S-11). The assertions below prove the dialog stays delete-free.

  function render(task: Task, updateImpl?: ReturnType<typeof vi.fn>) {
    configure(task, updateImpl);
    const fixture = TestBed.createComponent(TaskDetailComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('renders an editable form when canEdit', () => {
    const el = render(baseTask({ canEdit: true })).nativeElement as HTMLElement;
    const title = el.querySelector('#detail-title') as HTMLInputElement | null;
    expect(title).not.toBeNull();
    expect(title!.value).toBe('Take out bins');
  });

  it('renders static text (no form) when read-only', () => {
    const el = render(baseTask({ status: 'InProgress', canEdit: false, canDelete: false }))
      .nativeElement as HTMLElement;
    expect(el.querySelector('#detail-title')).toBeNull();
    expect(el.querySelector('.task-detail-readonly')).not.toBeNull();
    expect(el.textContent).toContain('Take out bins');
  });

  it('Save calls update with the form values and closes', () => {
    const fixture = render(baseTask({ canEdit: true }));
    const el = fixture.nativeElement as HTMLElement;

    const title = el.querySelector('#detail-title') as HTMLInputElement;
    title.value = 'Renamed';
    title.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    (el.querySelector('button[type="submit"]') as HTMLButtonElement).click();

    expect(update).toHaveBeenCalledWith('t1', {
      title: 'Renamed',
      description: undefined,
      tags: [],
    });
    expect(assign).not.toHaveBeenCalled();
    expect(close).toHaveBeenCalled();
  });

  it('is a pure form with no delete affordance, even when canDelete', () => {
    const el = render(baseTask({ canEdit: true, canDelete: true })).nativeElement as HTMLElement;

    // No Delete control lives in the dialog any more — deletion is on the card's ⋯ menu (S-11).
    expect(el.querySelector('.btn--danger')).toBeNull();
    expect(el.querySelector('.confirm-delete')).toBeNull();
    expect(el.textContent).not.toContain('Delete');
    // Cancel + Save are the only edit-form actions (the comments section has its own Post button below).
    const actions = Array.from(
      el.querySelectorAll('form:not(.comment-form) .modal-actions .btn'),
    ).map((b) => b.textContent?.trim());
    expect(actions).toEqual(['Cancel', 'Save changes']);
    expect(deleteTask).not.toHaveBeenCalled();
  });

  it('Cancel closes the dialog without saving', () => {
    const fixture = render(baseTask({ canEdit: true }));
    const el = fixture.nativeElement as HTMLElement;

    const cancel = Array.from(el.querySelectorAll('.modal-actions .btn')).find(
      (b) => b.textContent?.trim() === 'Cancel',
    ) as HTMLButtonElement;
    cancel.click();

    expect(update).not.toHaveBeenCalled();
    expect(close).toHaveBeenCalled();
  });

  it('maps a 400 and keeps the dialog open', () => {
    const fixture = render(
      baseTask({ canEdit: true }),
      vi.fn(() =>
        throwError(
          () =>
            new HttpErrorResponse({
              status: 400,
              error: { errors: { Title: ['A task title is required.'] } },
            }),
        ),
      ),
    );
    const el = fixture.nativeElement as HTMLElement;

    (el.querySelector('button[type="submit"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(close).not.toHaveBeenCalled();
    expect(el.querySelector('.form-error')?.textContent).toContain('A task title is required.');
  });

  // The comment thread moved to its own dialog (CommentsComponent, commit 26a676c); this panel is now a
  // pure edit form, so its former comment-thread tests live with that component, not here.

  it('a read-only (non-admin) member sees static fields and no comment composer here', () => {
    const el = render(baseTask({ status: 'InProgress', canEdit: false })).nativeElement as HTMLElement;
    expect(el.querySelector('#detail-title')).toBeNull();
    expect(el.querySelector('.task-detail-readonly')).not.toBeNull();
    expect(el.querySelector('.comment-form')).toBeNull();
  });

  it('shows the assignee picker only when the task is assignable (canAssign)', () => {
    expect(
      (render(baseTask({ canEdit: true, canAssign: false })).nativeElement as HTMLElement).querySelector(
        '#detail-assignee',
      ),
    ).toBeNull();
    expect(
      (render(baseTask({ canEdit: true, canAssign: true })).nativeElement as HTMLElement).querySelector(
        '#detail-assignee',
      ),
    ).not.toBeNull();
  });

  it('assigns after saving edits when a member is picked, and flashes for another person', () => {
    const fixture = render(baseTask({ canEdit: true, canAssign: true }));
    const component = fixture.componentInstance;
    component.form.controls.assigneeId.setValue('u2');

    component.save();

    expect(update).toHaveBeenCalledWith('t1', { title: 'Take out bins', description: undefined, tags: [] });
    expect(assign).toHaveBeenCalledWith('t1', 'u2');
    expect(show).toHaveBeenCalledWith(
      "Molly will be notified on any device where they've turned notifications on.",
    );
    expect(close).toHaveBeenCalled();
  });

  it('"Anyone" (empty pick) saves without assigning', () => {
    const fixture = render(baseTask({ canEdit: true, canAssign: true }));
    fixture.componentInstance.save();

    expect(update).toHaveBeenCalled();
    expect(assign).not.toHaveBeenCalled();
    expect(show).not.toHaveBeenCalled();
  });
});
