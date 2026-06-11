import { ComponentFixture, TestBed } from '@angular/core/testing';

import { TaskCardComponent } from './task-card.component';
import { Task } from '../task.service';

describe('TaskCardComponent', () => {
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
      canEdit: false,
      canDelete: false,
      canUnclaim: false,
      canSendBack: false,
      commentCount: 0,
      ...overrides,
    };
  }

  function render(task: Task): ComponentFixture<TaskCardComponent> {
    TestBed.configureTestingModule({ imports: [TaskCardComponent] });
    const fixture = TestBed.createComponent(TaskCardComponent);
    fixture.componentRef.setInput('task', task);
    fixture.detectChanges();
    return fixture;
  }

  it('renders the title and metadata (created by / claimed by / created)', () => {
    const el = render(baseTask({ claimerName: 'Arthur' })).nativeElement as HTMLElement;
    expect(el.querySelector('.task-title-button')?.textContent?.trim()).toBe('Take out bins');
    const meta = el.querySelector('.task-meta')?.textContent ?? '';
    expect(meta).toContain('Molly');
    expect(meta).toContain('Arthur');
    expect(el.querySelector('.task-meta')?.textContent).toContain('Created');
  });

  it('shows only the permitted primary lifecycle action', () => {
    const el = render(baseTask({ canClaim: true })).nativeElement as HTMLElement;
    const labels = Array.from(el.querySelectorAll('.task-action')).map((b) =>
      b.textContent?.trim(),
    );
    expect(labels).toEqual(['Claim']);
  });

  it('emits openDetail when the title is clicked', () => {
    const fixture = render(baseTask({}));
    const spy = vi.fn();
    fixture.componentInstance.openDetail.subscribe(spy);

    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('.task-title-button')!
      .click();

    expect(spy).toHaveBeenCalledWith(fixture.componentInstance.task);
  });

  it('emits the primary lifecycle action (claim) when clicked', () => {
    const fixture = render(baseTask({ canClaim: true }));
    const spy = vi.fn();
    fixture.componentInstance.claim.subscribe(spy);

    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('.task-action')!
      .click();

    expect(spy).toHaveBeenCalledWith(fixture.componentInstance.task);
  });

  it('the ⋯ menu shows Edit + Delete only when canEdit / canDelete, and emits them', () => {
    const fixture = render(baseTask({ canEdit: true, canDelete: true }));
    const el = fixture.nativeElement as HTMLElement;
    const editSpy = vi.fn();
    const deleteSpy = vi.fn();
    fixture.componentInstance.edit.subscribe(editSpy);
    fixture.componentInstance.delete.subscribe(deleteSpy);

    // Open the overflow menu.
    el.querySelector<HTMLButtonElement>('[aria-label="Task actions"]')!.click();
    fixture.detectChanges();

    const items = Array.from(el.querySelectorAll('.task-menu-item')).map((b) =>
      b.textContent?.trim(),
    );
    expect(items).toContain('Edit');
    expect(items).toContain('Delete');
    // S-05 loop-recovery slots only render when the affordance is granted — not for this To-do task.
    expect(items).not.toContain('Unclaim');
    expect(items).not.toContain('Send back');

    (
      Array.from(el.querySelectorAll('.task-menu-item')).find(
        (b) => b.textContent?.trim() === 'Edit',
      ) as HTMLButtonElement
    ).click();
    expect(editSpy).toHaveBeenCalledWith(fixture.componentInstance.task);

    // Reopen (Edit closed it) and click Delete.
    el.querySelector<HTMLButtonElement>('[aria-label="Task actions"]')!.click();
    fixture.detectChanges();
    (
      Array.from(el.querySelectorAll('.task-menu-item')).find(
        (b) => b.textContent?.trim() === 'Delete',
      ) as HTMLButtonElement
    ).click();
    expect(deleteSpy).toHaveBeenCalledWith(fixture.componentInstance.task);
  });

  it('hides Edit and Delete in the menu when the task is not editable/deletable', () => {
    const fixture = render(baseTask({ canEdit: false, canDelete: false }));
    const el = fixture.nativeElement as HTMLElement;

    el.querySelector<HTMLButtonElement>('[aria-label="Task actions"]')!.click();
    fixture.detectChanges();

    const items = Array.from(el.querySelectorAll('.task-menu-item')).map((b) =>
      b.textContent?.trim(),
    );
    expect(items).not.toContain('Edit');
    expect(items).not.toContain('Delete');
  });

  it('shows Unclaim only when canUnclaim and emits it on click', () => {
    const fixture = render(baseTask({ status: 'InProgress', canUnclaim: true }));
    const el = fixture.nativeElement as HTMLElement;
    const spy = vi.fn();
    fixture.componentInstance.unclaim.subscribe(spy);

    el.querySelector<HTMLButtonElement>('[aria-label="Task actions"]')!.click();
    fixture.detectChanges();

    const unclaim = Array.from(el.querySelectorAll('.task-menu-item')).find(
      (b) => b.textContent?.trim() === 'Unclaim',
    ) as HTMLButtonElement;
    expect(unclaim).toBeTruthy();

    unclaim.click();
    expect(spy).toHaveBeenCalledWith(fixture.componentInstance.task);
  });

  it('shows Send back only when canSendBack and emits it on click', () => {
    const fixture = render(baseTask({ status: 'Done', canSendBack: true }));
    const el = fixture.nativeElement as HTMLElement;
    const spy = vi.fn();
    fixture.componentInstance.sendBack.subscribe(spy);

    el.querySelector<HTMLButtonElement>('[aria-label="Task actions"]')!.click();
    fixture.detectChanges();

    const sendBack = Array.from(el.querySelectorAll('.task-menu-item')).find(
      (b) => b.textContent?.trim() === 'Send back',
    ) as HTMLButtonElement;
    expect(sendBack).toBeTruthy();

    sendBack.click();
    expect(spy).toHaveBeenCalledWith(fixture.componentInstance.task);
  });

  it('hides Unclaim and Send back when their affordances are false', () => {
    const fixture = render(
      baseTask({ status: 'InProgress', canUnclaim: false, canSendBack: false }),
    );
    const el = fixture.nativeElement as HTMLElement;

    el.querySelector<HTMLButtonElement>('[aria-label="Task actions"]')!.click();
    fixture.detectChanges();

    const items = Array.from(el.querySelectorAll('.task-menu-item')).map((b) =>
      b.textContent?.trim(),
    );
    expect(items).not.toContain('Unclaim');
    expect(items).not.toContain('Send back');
  });

  it('renders the 💬 comment count only when commentCount > 0', () => {
    const fixture = render(baseTask({ commentCount: 0 }));
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.task-comment-count')).toBeNull();

    fixture.componentRef.setInput('task', baseTask({ commentCount: 3 }));
    fixture.detectChanges();
    const badge = el.querySelector('.task-comment-count');
    expect(badge).not.toBeNull();
    expect(badge?.textContent).toContain('3');
  });
});
