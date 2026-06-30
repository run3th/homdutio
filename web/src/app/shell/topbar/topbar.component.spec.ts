import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { Dialog } from '@angular/cdk/dialog';
import { of } from 'rxjs';

import { TopbarComponent } from './topbar.component';
import { AuthService } from '../../auth/auth.service';
import { Household, HouseholdService } from '../../household/household.service';
import { TaskService } from '../../board/task.service';
import { CreateTaskComponent } from '../../board/create-task/create-task.component';
import { InviteDialogComponent } from './invite-dialog.component';

describe('TopbarComponent', () => {
  const current = signal<Household | null>({ id: 'h1', name: 'The Burrow', role: 'Admin' });
  let open: ReturnType<typeof vi.fn>;
  let setPaused: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    open = vi.fn(() => ({ closed: of(undefined) }));
    setPaused = vi.fn();
    TestBed.configureTestingModule({
      imports: [TopbarComponent],
      providers: [
        provideRouter([]),
        { provide: HouseholdService, useValue: { current } },
        { provide: Dialog, useValue: { open } },
        { provide: TaskService, useValue: { setPaused } },
        // The embedded avatar menu injects AuthService (identity signals + logout).
        {
          provide: AuthService,
          useValue: {
            email: signal('molly@burrow.test'),
            displayName: signal<string | null>('Molly Weasley'),
            avatarUrl: signal<string | null>(null),
            logout: vi.fn(),
          },
        },
      ],
    });
  });

  function render() {
    const fixture = TestBed.createComponent(TopbarComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('shows the household name and role badge', () => {
    const el = render().nativeElement as HTMLElement;
    expect(el.querySelector('.workspace-name')?.textContent).toContain('The Burrow');
    expect(el.querySelector('.role-badge')?.textContent).toContain('Admin');
  });

  it('Invite opens the invite dialog', () => {
    const fixture = render();
    const button = (fixture.nativeElement as HTMLElement).querySelector(
      '.btn-outline',
    ) as HTMLButtonElement;

    button.click();

    expect(open).toHaveBeenCalledWith(InviteDialogComponent);
  });

  it('+ Add task opens the create dialog and pauses/resumes board polling', () => {
    const fixture = render();
    const button = (fixture.nativeElement as HTMLElement).querySelector(
      '.btn-accent',
    ) as HTMLButtonElement;

    button.click();

    expect(open).toHaveBeenCalledWith(CreateTaskComponent);
    // Paused on open; the stubbed closed$ emits synchronously, so it resumes too.
    expect(setPaused).toHaveBeenCalledWith(true);
    expect(setPaused).toHaveBeenLastCalledWith(false);
  });
});
