import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { of } from 'rxjs';

import { BoardComponent } from './board.component';
import { Household, HouseholdService } from '../household/household.service';
import { Task, TaskService } from './task.service';

describe('BoardComponent', () => {
  const current = signal<Household | null>({ id: 'h1', name: 'The Burrow', role: 'Admin' });
  const tasks = signal<Task[]>([]);

  beforeEach(() => {
    tasks.set([]);
    TestBed.configureTestingModule({
      imports: [BoardComponent],
      providers: [
        { provide: HouseholdService, useValue: { current } },
        { provide: TaskService, useValue: { current: tasks.asReadonly(), load: () => of(tasks()) } },
      ],
    });
  });

  function render(): HTMLElement {
    const fixture = TestBed.createComponent(BoardComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('renders the household name and role badge', () => {
    const el = render();
    expect(el.querySelector('.board-header h1')?.textContent).toContain('The Burrow');
    expect(el.querySelector('.role-badge')?.textContent).toContain('Admin');
  });

  it('renders the three column labels', () => {
    const el = render();
    const titles = Array.from(el.querySelectorAll('.column-title')).map((n) => n.textContent?.trim());
    expect(titles).toEqual(['To do', 'In progress', 'Done']);
  });
});
