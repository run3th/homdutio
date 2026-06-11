import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { of, throwError } from 'rxjs';

import { TaskDetailComponent } from './task-detail.component';
import { Comment, Task, TaskService } from '../task.service';

describe('TaskDetailComponent', () => {
  let update: ReturnType<typeof vi.fn>;
  let deleteTask: ReturnType<typeof vi.fn>;
  let getComments: ReturnType<typeof vi.fn>;
  let addComment: ReturnType<typeof vi.fn>;
  let close: ReturnType<typeof vi.fn>;

  function baseTask(overrides: Partial<Task>): Task {
    return {
      id: 't1',
      title: 'Take out bins',
      description: null,
      category: null,
      status: 'ToDo',
      createdByName: 'Molly',
      claimerName: null,
      createdAtUtc: '2026-06-01T10:00:00Z',
      canClaim: false,
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

  function configure(task: Task) {
    update = vi.fn(() => of([]));
    deleteTask = vi.fn(() => of([]));
    getComments = vi.fn(() => of([]));
    addComment = vi.fn(() => of({}));
    close = vi.fn();
    TestBed.configureTestingModule({
      imports: [TaskDetailComponent],
      providers: [
        { provide: DIALOG_DATA, useValue: task },
        { provide: DialogRef, useValue: { close } },
        {
          provide: TaskService,
          useValue: { update, delete: deleteTask, getComments, addComment },
        },
      ],
    });
  }

  // `deleteTask` is provided only to satisfy the injector; the edit dialog never calls it (delete moved to
  // the card's ⋯ menu, S-11). The assertions below prove the dialog stays delete-free.

  function render(task: Task) {
    configure(task);
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
      category: undefined,
    });
    expect(close).toHaveBeenCalled();
  });

  it('is a pure form with no delete affordance, even when canDelete', () => {
    const el = render(baseTask({ canEdit: true, canDelete: true })).nativeElement as HTMLElement;

    // No Delete control lives in the dialog any more — deletion is on the card's ⋯ menu (S-11).
    expect(el.querySelector('.btn--danger')).toBeNull();
    expect(el.querySelector('.confirm-delete')).toBeNull();
    expect(el.textContent).not.toContain('Delete');
    // Cancel + Save are the only edit-form actions (the comments section has its own Post button below).
    const actions = Array.from(el.querySelectorAll('form:not(.comment-form) .actions .btn')).map(
      (b) => b.textContent?.trim(),
    );
    expect(actions).toEqual(['Cancel', 'Save']);
    expect(deleteTask).not.toHaveBeenCalled();
  });

  it('Cancel closes the dialog without saving', () => {
    const fixture = render(baseTask({ canEdit: true }));
    const el = fixture.nativeElement as HTMLElement;

    const cancel = Array.from(el.querySelectorAll('.actions .btn')).find(
      (b) => b.textContent?.trim() === 'Cancel',
    ) as HTMLButtonElement;
    cancel.click();

    expect(update).not.toHaveBeenCalled();
    expect(close).toHaveBeenCalled();
  });

  it('maps a 400 and keeps the dialog open', () => {
    update = vi.fn(() =>
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: { errors: { Title: ['A task title is required.'] } },
          }),
      ),
    );
    deleteTask = vi.fn(() => of([]));
    getComments = vi.fn(() => of([]));
    addComment = vi.fn(() => of({}));
    close = vi.fn();
    TestBed.configureTestingModule({
      imports: [TaskDetailComponent],
      providers: [
        { provide: DIALOG_DATA, useValue: baseTask({ canEdit: true }) },
        { provide: DialogRef, useValue: { close } },
        {
          provide: TaskService,
          useValue: { update, delete: deleteTask, getComments, addComment },
        },
      ],
    });
    const fixture = TestBed.createComponent(TaskDetailComponent);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    (el.querySelector('button[type="submit"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(close).not.toHaveBeenCalled();
    expect(el.querySelector('.form-error')?.textContent).toContain('A task title is required.');
  });

  // --- S-05: comments thread -----------------------------------------------------------------------

  function comment(overrides: Partial<Comment>): Comment {
    return {
      id: 'c1',
      body: 'Looks good',
      kind: 'Member',
      authorName: 'Molly',
      createdAtUtc: '2026-06-01T10:00:00Z',
      ...overrides,
    };
  }

  function renderWithComments(comments: Comment[], task = baseTask({ canEdit: true })) {
    configure(task);
    getComments.mockReturnValue(of(comments));
    const fixture = TestBed.createComponent(TaskDetailComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('lists comments with author/time and flags the send-back kind', () => {
    const el = renderWithComments([
      comment({ id: 'c1', body: 'On it', authorName: 'Molly', kind: 'Member' }),
      comment({ id: 'c2', body: 'Please redo', authorName: 'Arthur', kind: 'SendBack' }),
    ]).nativeElement as HTMLElement;

    expect(el.querySelectorAll('.comment').length).toBe(2);
    expect(el.textContent).toContain('Molly');
    expect(el.textContent).toContain('On it');
    expect(el.querySelector('.comment--sendback')).not.toBeNull();
    expect(el.querySelector('.comment-tag')?.textContent?.trim()).toBe('Sent back');
  });

  it('shows the empty state when there are no comments', () => {
    const el = renderWithComments([]).nativeElement as HTMLElement;
    expect(el.querySelector('.comments-empty')?.textContent).toContain('No comments yet');
  });

  it('disables Post and does not call addComment while the input is empty', () => {
    const fixture = renderWithComments([]);
    const el = fixture.nativeElement as HTMLElement;
    const post = el.querySelector('.comment-form button[type="submit"]') as HTMLButtonElement;
    expect(post.disabled).toBe(true);

    fixture.componentInstance.postComment();
    expect(addComment).not.toHaveBeenCalled();
  });

  it('a valid post calls addComment and re-lists the thread', () => {
    const fixture = renderWithComments([]);
    expect(getComments).toHaveBeenCalledTimes(1);

    fixture.componentInstance.newComment.setValue('Nice work');
    fixture.componentInstance.postComment();

    expect(addComment).toHaveBeenCalledWith('t1', 'Nice work');
    // The thread re-lists after a successful post.
    expect(getComments).toHaveBeenCalledTimes(2);
  });

  it('an admin (canEdit) sees the edit form and the comment input', () => {
    const el = renderWithComments([], baseTask({ canEdit: true })).nativeElement as HTMLElement;
    expect(el.querySelector('#detail-title')).not.toBeNull();
    expect(el.querySelector('.comment-form textarea')).not.toBeNull();
  });

  it('a read-only (non-admin) member sees static fields but can still comment', () => {
    const el = renderWithComments([], baseTask({ status: 'InProgress', canEdit: false }))
      .nativeElement as HTMLElement;
    expect(el.querySelector('#detail-title')).toBeNull();
    expect(el.querySelector('.task-detail-readonly')).not.toBeNull();
    expect(el.querySelector('.comment-form textarea')).not.toBeNull();
  });
});
