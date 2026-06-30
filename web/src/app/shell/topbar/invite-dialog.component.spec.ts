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

  it('does not send an invite email for an invalid address', () => {
    const fixture = render();
    generate.mockClear();

    fixture.componentInstance.email.setValue('not-an-email');
    fixture.componentInstance.sendEmail();

    expect(generate).not.toHaveBeenCalled();
    expect(fixture.componentInstance.email.touched).toBe(true);
    expect(fixture.componentInstance.sentTo()).toBeNull();
  });

  it('sends the invite email and confirms the recipient', () => {
    const fixture = render();
    generate.mockClear();

    fixture.componentInstance.email.setValue('joiner@example.com');
    fixture.componentInstance.sendEmail();

    expect(generate).toHaveBeenCalledWith('joiner@example.com');
    expect(fixture.componentInstance.sentTo()).toBe('joiner@example.com');
    expect(fixture.componentInstance.sending()).toBe(false);
    expect(fixture.componentInstance.email.value).toBe('');
  });

  it('surfaces an error when the invite email cannot be sent', () => {
    const fixture = render();
    generate.mockReturnValue(throwError(() => new Error('boom')));

    fixture.componentInstance.email.setValue('joiner@example.com');
    fixture.componentInstance.sendEmail();

    expect(fixture.componentInstance.sendError()).toBeTruthy();
    expect(fixture.componentInstance.sentTo()).toBeNull();
  });

  it('Close dismisses the dialog', () => {
    const fixture = render();
    fixture.componentInstance.close();
    expect(close).toHaveBeenCalled();
  });
});
