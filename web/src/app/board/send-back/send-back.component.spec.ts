import { TestBed } from '@angular/core/testing';
import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';

import { SendBackComponent } from './send-back.component';
import { Task } from '../task.service';

describe('SendBackComponent', () => {
  let close: ReturnType<typeof vi.fn>;

  function baseTask(): Task {
    return {
      id: 't1',
      title: 'Take out bins',
      description: null,
      tags: [],
      status: 'Done',
      createdByName: 'Molly',
      claimerName: 'Arthur',
      createdAtUtc: '2026-06-01T10:00:00Z',
      canClaim: false,
      canMarkDone: false,
      canConfirm: false,
      willSelfAttest: false,
      canEdit: true,
      canDelete: false,
      canUnclaim: false,
      canSendBack: true,
      commentCount: 0,
    };
  }

  function render() {
    close = vi.fn();
    TestBed.configureTestingModule({
      imports: [SendBackComponent],
      providers: [
        { provide: DIALOG_DATA, useValue: baseTask() },
        { provide: DialogRef, useValue: { close } },
      ],
    });
    const fixture = TestBed.createComponent(SendBackComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('shows the task title and claimer in the prompt', () => {
    const el = render().nativeElement as HTMLElement;
    expect(el.textContent).toContain('Take out bins');
    expect(el.textContent).toContain('Arthur');
  });

  it('disables Send back and does not close while the reason is empty', () => {
    const fixture = render();
    const el = fixture.nativeElement as HTMLElement;
    const sendBack = Array.from(el.querySelectorAll('button')).find(
      (b) => b.textContent?.trim() === 'Send back',
    ) as HTMLButtonElement;

    expect(sendBack.disabled).toBe(true);

    // Even calling submit directly with a blank value must not close the dialog.
    fixture.componentInstance.submit();
    expect(close).not.toHaveBeenCalled();
  });

  it('closes with the trimmed reason on submit', () => {
    const fixture = render();
    fixture.componentInstance.comment.setValue('  Please redo the corners  ');

    fixture.componentInstance.submit();

    expect(close).toHaveBeenCalledWith('Please redo the corners');
  });

  it('Cancel closes the dialog with undefined', () => {
    const fixture = render();
    const cancel = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('button'),
    ).find((b) => b.textContent?.trim() === 'Cancel') as HTMLButtonElement;

    cancel.click();

    expect(close).toHaveBeenCalledWith(undefined);
  });
});
