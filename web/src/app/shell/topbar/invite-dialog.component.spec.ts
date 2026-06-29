import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { DialogRef } from '@angular/cdk/dialog';
import { of, throwError } from 'rxjs';

import { InviteDialogComponent } from './invite-dialog.component';
import { Household, HouseholdService } from '../../household/household.service';
import { InviteService } from '../../household/invite.service';

describe('InviteDialogComponent', () => {
  const current = signal<Household | null>({ id: 'h1', name: 'The Burrow', role: 'Admin' });
  let generate: ReturnType<typeof vi.fn>;
  let writeText: ReturnType<typeof vi.fn>;
  let close: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    generate = vi.fn(() => of({ token: 'tok123', expiresAtUtc: '2026-06-09T00:00:00Z' }));
    writeText = vi.fn(() => Promise.resolve());
    close = vi.fn();
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    TestBed.configureTestingModule({
      imports: [InviteDialogComponent],
      providers: [
        { provide: HouseholdService, useValue: { current } },
        { provide: InviteService, useValue: { generate } },
        { provide: DialogRef, useValue: { close } },
      ],
    });
  });

  function render() {
    const fixture = TestBed.createComponent(InviteDialogComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('mints a token on open and exposes the copyable link', () => {
    const fixture = render();

    expect(generate).toHaveBeenCalled();
    const expected = `${window.location.origin}/join/tok123`;
    expect(fixture.componentInstance.inviteLink()).toBe(expected);
    expect((fixture.nativeElement as HTMLElement).querySelector('.invite-link')?.textContent).toBe(
      expected,
    );
  });

  it('Copy writes the link to the clipboard and confirms', async () => {
    const fixture = render();

    fixture.componentInstance.copy();
    await Promise.resolve();

    const expected = `${window.location.origin}/join/tok123`;
    expect(writeText).toHaveBeenCalledWith(expected);
    expect(fixture.componentInstance.inviteCopied()).toBe(true);
  });

  it('surfaces an error when the token cannot be minted', () => {
    generate.mockReturnValue(throwError(() => new Error('boom')));
    const fixture = render();

    expect(fixture.componentInstance.inviteError()).toBeTruthy();
    expect(fixture.componentInstance.inviteLink()).toBeNull();
  });

  it('Close dismisses the dialog', () => {
    const fixture = render();
    fixture.componentInstance.close();
    expect(close).toHaveBeenCalled();
  });
});
