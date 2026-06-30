import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, provideRouter, Router } from '@angular/router';
import { of, throwError } from 'rxjs';

import { JoinComponent } from './join.component';
import { AuthService } from '../auth/auth.service';
import { Household, HouseholdService } from '../household/household.service';
import { InviteService } from '../household/invite.service';

describe('JoinComponent', () => {
  const TOKEN = 'abc123';

  let preview: ReturnType<typeof vi.fn>;
  let accept: ReturnType<typeof vi.fn>;
  let loadMine: ReturnType<typeof vi.fn>;
  let setMembership: ReturnType<typeof vi.fn>;
  let navSpy: ReturnType<typeof vi.spyOn>;

  const isAuthenticated = signal(false);
  const current = signal<Household | null>(null);
  const loaded = signal(false);

  beforeEach(() => {
    isAuthenticated.set(false);
    current.set(null);
    loaded.set(false);
    preview = vi.fn(() => of({ householdName: 'The Burrow', inviterName: 'Robin', inviterId: 'inv1' }));
    accept = vi.fn();
    loadMine = vi.fn(() => of(current()));
    setMembership = vi.fn();

    TestBed.configureTestingModule({
      imports: [JoinComponent],
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: new Map([['token', TOKEN]]) } },
        },
        { provide: InviteService, useValue: { preview, accept } },
        { provide: AuthService, useValue: { isAuthenticated } },
        {
          provide: HouseholdService,
          useValue: { current, loaded, loadMine, setMembership },
        },
      ],
    });
    navSpy = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  });

  function render() {
    const fixture = TestBed.createComponent(JoinComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('logged-out valid preview: shows the joinLoggedOut screen naming inviter + household', () => {
    const fixture = render();
    const el = fixture.nativeElement as HTMLElement;
    expect(preview).toHaveBeenCalledWith(TOKEN);
    expect(fixture.componentInstance.screen()).toBe('joinLoggedOut');
    expect(el.querySelector('h1')?.textContent).toContain('The Burrow');
    expect(el.textContent).toContain('Robin');
  });

  it('shows the invalid message when preview returns 410', () => {
    preview.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 410 })));
    const el = render().nativeElement as HTMLElement;
    expect(el.querySelector('.form-error')?.textContent).toContain('no longer valid');
  });

  it('shows the invalid message when preview returns 404', () => {
    preview.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 })));
    const el = render().nativeElement as HTMLElement;
    expect(el.querySelector('.form-error')?.textContent).toContain('no longer valid');
  });

  it('logged-out: the Log in action carries a returnUrl back to the join page', () => {
    isAuthenticated.set(false);
    const el = render().nativeElement as HTMLElement;
    const login = el.querySelector('a[href*="/login"]') as HTMLAnchorElement;
    expect(login.getAttribute('href')).toContain('returnUrl=%2Fjoin%2Fabc123');
  });

  it('authenticated with no household: shows the join screen and Accept joins + routes to the board', () => {
    isAuthenticated.set(true);
    loaded.set(true);
    current.set(null);
    const household: Household = { id: 'h1', name: 'The Burrow', role: 'Member' };
    accept.mockReturnValue(of(household));

    const fixture = render();
    expect(fixture.componentInstance.screen()).toBe('join');
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Accept & join The Burrow');

    fixture.componentInstance.join();

    expect(accept).toHaveBeenCalledWith(TOKEN);
    expect(setMembership).toHaveBeenCalledWith(household);
    expect(navSpy).toHaveBeenCalledWith(['/board']);
  });

  it('authenticated, no household, not yet loaded: stays on the loading screen (no premature join)', () => {
    isAuthenticated.set(true);
    loaded.set(false);
    current.set(null);

    const fixture = render();
    expect(fixture.componentInstance.screen()).toBe('loading');
  });

  it('authenticated and already in a household: shows the calm joinTaken screen (non-error)', () => {
    isAuthenticated.set(true);
    loaded.set(true);
    current.set({ id: 'h9', name: 'Other House', role: 'Admin' });

    const fixture = render();
    const el = fixture.nativeElement as HTMLElement;
    expect(fixture.componentInstance.screen()).toBe('joinTaken');
    expect(el.textContent).toContain("You're already in");
    expect(el.querySelector('.form-error')).toBeNull(); // calm, not an error
    const board = el.querySelector('a[href*="/board"]') as HTMLAnchorElement;
    expect(board).not.toBeNull();
  });

  it('a join-time 410 falls back to the invalid screen', () => {
    isAuthenticated.set(true);
    loaded.set(true);
    current.set(null);
    accept.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 410 })));

    const fixture = render();
    fixture.componentInstance.join();
    fixture.detectChanges();

    expect(fixture.componentInstance.previewState()).toBe('invalid');
    expect(navSpy).not.toHaveBeenCalled();
  });
});
