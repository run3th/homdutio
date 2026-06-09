import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { TopbarComponent } from './topbar.component';
import { AuthService } from '../../auth/auth.service';
import { Household, HouseholdService } from '../../household/household.service';
import { InviteService } from '../../household/invite.service';

describe('TopbarComponent', () => {
  const current = signal<Household | null>({ id: 'h1', name: 'The Burrow', role: 'Admin' });
  let generate: ReturnType<typeof vi.fn>;
  let writeText: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    generate = vi.fn(() => of({ token: 'tok123', expiresAtUtc: '2026-06-09T00:00:00Z' }));
    writeText = vi.fn(() => Promise.resolve());
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    TestBed.configureTestingModule({
      imports: [TopbarComponent],
      providers: [
        provideRouter([]),
        { provide: HouseholdService, useValue: { current } },
        { provide: InviteService, useValue: { generate } },
        // The embedded avatar menu injects AuthService.
        { provide: AuthService, useValue: { email: signal('molly@burrow.test'), logout: vi.fn() } },
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
    expect(el.querySelector('.topbar-title')?.textContent).toContain('The Burrow');
    expect(el.querySelector('.role-badge')?.textContent).toContain('Admin');
  });

  it('Invite a member generates a token and exposes the copyable link', async () => {
    const fixture = render();
    const button = (fixture.nativeElement as HTMLElement).querySelector(
      '.invite-button',
    ) as HTMLButtonElement;

    button.click();
    await Promise.resolve();
    fixture.detectChanges();

    expect(generate).toHaveBeenCalled();
    const expected = `${window.location.origin}/join/tok123`;
    expect(writeText).toHaveBeenCalledWith(expected);
    expect(fixture.componentInstance.inviteLink()).toBe(expected);
    expect((fixture.nativeElement as HTMLElement).querySelector('.invite-link')?.textContent).toBe(
      expected,
    );
  });
});
