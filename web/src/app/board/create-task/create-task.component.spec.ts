import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { DialogRef } from '@angular/cdk/dialog';
import { of, throwError } from 'rxjs';

import { CreateTaskComponent } from './create-task.component';
import { TaskService } from '../task.service';
import { Member, MemberService } from '../../household/member.service';
import { HouseholdService } from '../../household/household.service';
import { FlashService } from '../../shared/flash/flash.service';

describe('CreateTaskComponent', () => {
  let create: ReturnType<typeof vi.fn>;
  let close: ReturnType<typeof vi.fn>;
  let getTagSuggestions: ReturnType<typeof vi.fn>;
  let list: ReturnType<typeof vi.fn>;
  let show: ReturnType<typeof vi.fn>;

  const roster: Member[] = [
    { userId: 'me', displayName: 'Rafał', email: 'r@x', role: 'Admin', isSelf: true, canManage: false },
    { userId: 'u2', displayName: 'Molly', email: 'm@x', role: 'Member', isSelf: false, canManage: true },
  ];

  function configure(role: 'Admin' | 'Member' | null) {
    // Reset so a single test may reconfigure with a different role (the picker-visibility test does).
    TestBed.resetTestingModule();
    create = vi.fn(() => of([]));
    close = vi.fn();
    getTagSuggestions = vi.fn(() => of([]));
    list = vi.fn(() => of(roster));
    show = vi.fn();
    TestBed.configureTestingModule({
      imports: [CreateTaskComponent],
      providers: [
        { provide: TaskService, useValue: { create, getTagSuggestions } },
        { provide: DialogRef, useValue: { close } },
        { provide: MemberService, useValue: { list } },
        { provide: HouseholdService, useValue: { current: () => (role ? { id: 'h', name: 'H', role } : null) } },
        { provide: FlashService, useValue: { show } },
      ],
    });
  }

  function instance(role: 'Admin' | 'Member' | null = 'Member') {
    configure(role);
    const fixture = TestBed.createComponent(CreateTaskComponent);
    fixture.detectChanges();
    return { fixture, component: fixture.componentInstance };
  }

  it('blocks submit and does not call create when the title is empty', () => {
    const { component } = instance();
    component.submit();
    expect(create).not.toHaveBeenCalled();
  });

  it('creates the task (trimmed, optionals omitted when blank) and closes the dialog on success', () => {
    const { component } = instance();
    component.form.setValue({ title: '  Take out bins  ', description: '', tags: [], assigneeId: '' });

    component.submit();

    expect(create).toHaveBeenCalledWith({
      title: 'Take out bins',
      description: undefined,
      tags: [],
      assigneeId: undefined,
    });
    expect(close).toHaveBeenCalled();
  });

  it('passes through description and tags when provided', () => {
    const { component } = instance();
    component.form.setValue({ title: 'Mow lawn', description: 'Front only', tags: ['Garden'], assigneeId: '' });

    component.submit();

    expect(create).toHaveBeenCalledWith({
      title: 'Mow lawn',
      description: 'Front only',
      tags: ['Garden'],
      assigneeId: undefined,
    });
  });

  it('maps 400 validation messages and keeps the dialog open', () => {
    const { component } = instance();
    create.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: { errors: { Title: ['A task title is required.'] } },
          }),
      ),
    );
    component.form.setValue({ title: 'x', description: '', tags: [], assigneeId: '' });

    component.submit();

    expect(component.errors()).toEqual(['A task title is required.']);
    expect(close).not.toHaveBeenCalled();
  });

  it('Cancel closes the dialog without creating', () => {
    const { component } = instance();
    component.close();
    expect(create).not.toHaveBeenCalled();
    expect(close).toHaveBeenCalled();
  });

  it('shows the assignee picker only for an admin', () => {
    const memberEl = instance('Member').fixture.nativeElement as HTMLElement;
    expect(memberEl.querySelector('#task-assignee')).toBeNull();

    const adminEl = instance('Admin').fixture.nativeElement as HTMLElement;
    expect(adminEl.querySelector('#task-assignee')).not.toBeNull();
  });

  it('includes the picked assignee id in the create call (admin)', () => {
    const { component } = instance('Admin');
    component.form.setValue({ title: 'Dishes', description: '', tags: [], assigneeId: 'u2' });

    component.submit();

    expect(create).toHaveBeenCalledWith({
      title: 'Dishes',
      description: undefined,
      tags: [],
      assigneeId: 'u2',
    });
    // Assigning to another person flashes the per-device reminder.
    expect(show).toHaveBeenCalledWith(
      "Molly will be notified on any device where they've turned notifications on.",
    );
  });

  it('"Anyone" (empty pick) creates an unassigned task with no flash', () => {
    const { component } = instance('Admin');
    component.form.setValue({ title: 'Dishes', description: '', tags: [], assigneeId: '' });

    component.submit();

    expect(create).toHaveBeenCalledWith({
      title: 'Dishes',
      description: undefined,
      tags: [],
      assigneeId: undefined,
    });
    expect(show).not.toHaveBeenCalled();
  });

  it('self-assignment does not flash the "will be notified" reminder (push toast is Phase 4)', () => {
    const { component } = instance('Admin');
    component.form.setValue({ title: 'Dishes', description: '', tags: [], assigneeId: 'me' });

    component.submit();

    expect(create).toHaveBeenCalledWith({
      title: 'Dishes',
      description: undefined,
      tags: [],
      assigneeId: 'me',
    });
    expect(show).not.toHaveBeenCalled();
  });
});
