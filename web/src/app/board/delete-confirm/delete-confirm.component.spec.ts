import { TestBed } from '@angular/core/testing';
import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';

import { DeleteConfirmComponent } from './delete-confirm.component';
import { Task } from '../task.service';

describe('DeleteConfirmComponent', () => {
  let close: ReturnType<typeof vi.fn>;

  function baseTask(): Task {
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
    };
  }

  function render() {
    close = vi.fn();
    TestBed.configureTestingModule({
      imports: [DeleteConfirmComponent],
      providers: [
        { provide: DIALOG_DATA, useValue: baseTask() },
        { provide: DialogRef, useValue: { close } },
      ],
    });
    const fixture = TestBed.createComponent(DeleteConfirmComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('shows the task title in the prompt', () => {
    const el = render().nativeElement as HTMLElement;
    expect(el.textContent).toContain('Take out bins');
  });

  it('Delete closes the dialog with true', () => {
    const fixture = render();
    const del = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find(
      (b) => b.textContent?.trim() === 'Delete',
    ) as HTMLButtonElement;

    del.click();

    expect(close).toHaveBeenCalledWith(true);
  });

  it('Cancel closes the dialog with false', () => {
    const fixture = render();
    const cancel = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('button'),
    ).find((b) => b.textContent?.trim() === 'Cancel') as HTMLButtonElement;

    cancel.click();

    expect(close).toHaveBeenCalledWith(false);
  });
});
