import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { Dialog } from '@angular/cdk/dialog';
import { of } from 'rxjs';

import { BoardComponent } from './board.component';
import { Household, HouseholdService } from '../household/household.service';
import { Task, TaskService } from './task.service';

describe('BoardComponent', () => {
  const current = signal<Household | null>({ id: 'h1', name: 'The Burrow', role: 'Admin' });
  const tasks = signal<Task[]>([]);
  let load: ReturnType<typeof vi.fn>;
  let claim: ReturnType<typeof vi.fn>;
  let markDone: ReturnType<typeof vi.fn>;
  let confirm: ReturnType<typeof vi.fn>;
  let reorder: ReturnType<typeof vi.fn>;
  let open: ReturnType<typeof vi.fn>;

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
      ...overrides,
    };
  }

  beforeEach(() => {
    tasks.set([]);
    load = vi.fn(() => of(tasks()));
    claim = vi.fn(() => of(tasks()));
    markDone = vi.fn(() => of(tasks()));
    confirm = vi.fn(() => of(tasks()));
    reorder = vi.fn(() => of(tasks()));
    open = vi.fn();
    TestBed.configureTestingModule({
      imports: [BoardComponent],
      providers: [
        { provide: HouseholdService, useValue: { current } },
        {
          provide: TaskService,
          useValue: { current: tasks.asReadonly(), load, claim, markDone, confirm, reorder },
        },
        { provide: Dialog, useValue: { open } },
      ],
    });
  });

  function render() {
    const fixture = TestBed.createComponent(BoardComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('renders the household name and role badge', () => {
    const el = render().nativeElement as HTMLElement;
    expect(el.querySelector('.board-header h1')?.textContent).toContain('The Burrow');
    expect(el.querySelector('.role-badge')?.textContent).toContain('Admin');
  });

  it('renders the three column labels and loads on init', () => {
    const el = render().nativeElement as HTMLElement;
    const titles = Array.from(el.querySelectorAll('.column-title')).map((n) =>
      n.textContent?.trim(),
    );
    expect(titles).toEqual(['To do', 'In progress', 'Done']);
    expect(load).toHaveBeenCalled();
  });

  it('groups tasks into the correct columns', () => {
    tasks.set([
      baseTask({ id: 'a', status: 'ToDo' }),
      baseTask({ id: 'b', status: 'InProgress' }),
      baseTask({ id: 'c', status: 'Done' }),
    ]);
    const el = render().nativeElement as HTMLElement;
    const columns = el.querySelectorAll('.column');
    expect(columns[0].querySelectorAll('.task-card').length).toBe(1);
    expect(columns[1].querySelectorAll('.task-card').length).toBe(1);
    expect(columns[2].querySelectorAll('.task-card').length).toBe(1);
  });

  it('renders only the affordance-permitted buttons', () => {
    tasks.set([baseTask({ id: 'a', status: 'ToDo', canClaim: true })]);
    const el = render().nativeElement as HTMLElement;
    const labels = Array.from(el.querySelectorAll('.task-action')).map((b) =>
      b.textContent?.trim(),
    );
    expect(labels).toEqual(['Claim']);
  });

  it('labels a self-attested confirm distinctly', () => {
    tasks.set([baseTask({ id: 'a', status: 'Done', canConfirm: true, willSelfAttest: true })]);
    const el = render().nativeElement as HTMLElement;
    expect(el.querySelector('.task-action')?.textContent?.trim()).toBe('Confirm (self-attested)');
  });

  it('a plain confirm (not self-attested) has no hint', () => {
    tasks.set([baseTask({ id: 'a', status: 'Done', canConfirm: true, willSelfAttest: false })]);
    const el = render().nativeElement as HTMLElement;
    expect(el.querySelector('.task-action')?.textContent?.trim()).toBe('Confirm');
  });

  it('clicking Claim calls the service', () => {
    tasks.set([baseTask({ id: 'a', status: 'ToDo', canClaim: true })]);
    const fixture = render();
    const button = (fixture.nativeElement as HTMLElement).querySelector(
      '.task-action',
    ) as HTMLButtonElement;

    button.click();

    expect(claim).toHaveBeenCalledWith('a');
  });

  it('clicking the task title opens the detail dialog with the task', () => {
    const task = baseTask({ id: 'a', status: 'ToDo', canEdit: true, canDelete: true });
    tasks.set([task]);
    const fixture = render();
    const titleButton = (fixture.nativeElement as HTMLElement).querySelector(
      '.task-title-button',
    ) as HTMLButtonElement;

    titleButton.click();

    expect(open).toHaveBeenCalledTimes(1);
    expect(open.mock.calls[0][1]).toEqual({ data: task });
  });

  it('a drop reorders the column and calls reorder with the new ordered ids', () => {
    tasks.set([
      baseTask({ id: 'a', status: 'ToDo' }),
      baseTask({ id: 'b', status: 'ToDo' }),
      baseTask({ id: 'c', status: 'ToDo' }),
    ]);
    const fixture = render();

    // Drag the third card (index 2) to the top (index 0).
    fixture.componentInstance.drop('ToDo', { previousIndex: 2, currentIndex: 0 } as never);

    expect(reorder).toHaveBeenCalledWith('ToDo', ['c', 'a', 'b']);
  });

  it('a no-op drop (same index) does not call reorder', () => {
    tasks.set([baseTask({ id: 'a', status: 'ToDo' }), baseTask({ id: 'b', status: 'ToDo' })]);
    const fixture = render();

    fixture.componentInstance.drop('ToDo', { previousIndex: 1, currentIndex: 1 } as never);

    expect(reorder).not.toHaveBeenCalled();
  });

  it('a confirmed task drops off the board after the refetch', () => {
    tasks.set([baseTask({ id: 'a', status: 'Done', canConfirm: true })]);
    // The service's confirm refetches; simulate the closed task no longer returning.
    confirm.mockImplementation(() => {
      tasks.set([]);
      return of(tasks());
    });
    const fixture = render();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelectorAll('.task-card').length).toBe(1);

    fixture.componentInstance.confirm(baseTask({ id: 'a', status: 'Done', canConfirm: true }));
    fixture.detectChanges();

    expect(el.querySelectorAll('.task-card').length).toBe(0);
  });
});
