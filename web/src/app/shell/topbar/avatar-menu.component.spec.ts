import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideRouter, Router } from '@angular/router';
import { Dialog } from '@angular/cdk/dialog';

import { AvatarMenuComponent } from './avatar-menu.component';
import { AuthService } from '../../auth/auth.service';
import { SettingsDialogComponent } from '../settings-dialog/settings-dialog.component';

describe('AvatarMenuComponent', () => {
  const email = signal<string | null>('molly@burrow.test');
  const displayName = signal<string | null>(null);
  const avatarUrl = signal<string | null>(null);
  let logout: ReturnType<typeof vi.fn>;
  let open: ReturnType<typeof vi.fn>;
  let navigateSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    email.set('molly@burrow.test');
    displayName.set(null);
    avatarUrl.set(null);
    logout = vi.fn();
    open = vi.fn();
    TestBed.configureTestingModule({
      imports: [AvatarMenuComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { email, displayName, avatarUrl, logout } },
        { provide: Dialog, useValue: { open } },
      ],
    });
    navigateSpy = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  });

  function render() {
    const fixture = TestBed.createComponent(AvatarMenuComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('shows the avatar initial (email fallback) and is closed by default', () => {
    const fixture = render();
    const el = fixture.nativeElement as HTMLElement;
    // With no display name yet, the avatar falls back to the email's first initial.
    expect(el.querySelector('.avatar-button')?.textContent?.trim()).toBe('M');
    expect(el.querySelector('.avatar-menu')).toBeNull();
    expect(el.querySelector('.avatar-button')?.getAttribute('aria-expanded')).toBe('false');
  });

  it('toggles the menu open, showing the display name and email', () => {
    displayName.set('Molly Weasley');
    const fixture = render();
    const button = (fixture.nativeElement as HTMLElement).querySelector(
      '.avatar-button',
    ) as HTMLButtonElement;

    button.click();
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.avatar-menu')).not.toBeNull();
    expect(el.querySelector('.avatar-menu-name')?.textContent?.trim()).toBe('Molly Weasley');
    expect(el.querySelector('.avatar-menu-email')?.textContent?.trim()).toBe('molly@burrow.test');
    expect(button.getAttribute('aria-expanded')).toBe('true');
  });

  it('Settings opens the settings dialog and closes the menu', () => {
    const fixture = render();
    fixture.componentInstance.open.set(true);
    fixture.detectChanges();

    fixture.componentInstance.openSettings();

    expect(open).toHaveBeenCalledWith(SettingsDialogComponent);
    expect(fixture.componentInstance.open()).toBe(false);
  });

  it('Log out calls AuthService.logout() and navigates to /login', () => {
    const fixture = render();
    fixture.componentInstance.open.set(true);
    fixture.detectChanges();

    const item = (fixture.nativeElement as HTMLElement).querySelector(
      '.avatar-menu-item--danger',
    ) as HTMLButtonElement;
    item.click();

    expect(logout).toHaveBeenCalledTimes(1);
    expect(navigateSpy).toHaveBeenCalledWith(['/login']);
  });

  it('closes on Escape', () => {
    const fixture = render();
    fixture.componentInstance.open.set(true);
    fixture.componentInstance.onEscape();
    expect(fixture.componentInstance.open()).toBe(false);
  });
});
