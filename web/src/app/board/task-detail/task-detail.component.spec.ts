import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { of, throwError } from 'rxjs';

import { TaskDetailComponent } from './task-detail.component';
import { Task, TaskService } from '../task.service';

describe('TaskDetailComponent', () => {
  let update: ReturnType<typeof vi.fn>;
  let deleteTask: ReturnType<typeof vi.fn>;
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
      ...overrides,
    };
  }

  function configure(task: Task) {
    update = vi.fn(() => of([]));
    deleteTask = vi.fn(() => of([]));
    close = vi.fn();
    TestBed.configureTestingModule({
      imports: [TaskDetailComponent],
      providers: [
        { provide: DIALOG_DATA, useValue: task },
        { provide: DialogRef, useValue: { close } },
        { provide: TaskService, useValue: { update, delete: deleteTask } },
      ],
    });
  }

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
    const el = render(
      baseTask({ status: 'InProgress', canEdit: false, canDelete: false }),
    ).nativeElement as HTMLElement;
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

  it('a two-step delete confirm calls delete and closes', () => {
    const fixture = render(baseTask({ canEdit: true, canDelete: true }));
    const el = fixture.nativeElement as HTMLElement;

    // First click arms the inline confirm.
    (el.querySelector('.btn--danger') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el.querySelector('.confirm-delete')).not.toBeNull();
    expect(deleteTask).not.toHaveBeenCalled();

    // The confirm button performs the delete.
    const confirmBtn = Array.from(el.querySelectorAll('.confirm-delete .btn--danger')).at(
      0,
    ) as HTMLButtonElement;
    confirmBtn.click();

    expect(deleteTask).toHaveBeenCalledWith('t1');
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
    close = vi.fn();
    TestBed.configureTestingModule({
      imports: [TaskDetailComponent],
      providers: [
        { provide: DIALOG_DATA, useValue: baseTask({ canEdit: true }) },
        { provide: DialogRef, useValue: { close } },
        { provide: TaskService, useValue: { update, delete: deleteTask } },
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
});
