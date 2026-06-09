import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideRouter, Router } from '@angular/router';

import { AvatarMenuComponent } from './avatar-menu.component';
import { AuthService } from '../../auth/auth.service';

describe('AvatarMenuComponent', () => {
  const email = signal<string | null>('molly@burrow.test');
  let logout: ReturnType<typeof vi.fn>;
  let navigateSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    logout = vi.fn();
    TestBed.configureTestingModule({
      imports: [AvatarMenuComponent],
      providers: [provideRouter([]), { provide: AuthService, useValue: { email, logout } }],
    });
    navigateSpy = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  });

  function render() {
    const fixture = TestBed.createComponent(AvatarMenuComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('shows the email initial and is closed by default', () => {
    const fixture = render();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.avatar-button')?.textContent?.trim()).toBe('M');
    expect(el.querySelector('.avatar-menu')).toBeNull();
    expect(el.querySelector('.avatar-button')?.getAttribute('aria-expanded')).toBe('false');
  });

  it('toggles the menu open, revealing the email', () => {
    const fixture = render();
    const button = (fixture.nativeElement as HTMLElement).querySelector(
      '.avatar-button',
    ) as HTMLButtonElement;

    button.click();
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.avatar-menu')).not.toBeNull();
    expect(el.querySelector('.avatar-menu-email')?.textContent?.trim()).toBe('molly@burrow.test');
    expect(button.getAttribute('aria-expanded')).toBe('true');
  });

  it('Log out calls AuthService.logout() and navigates to /login', () => {
    const fixture = render();
    fixture.componentInstance.open.set(true);
    fixture.detectChanges();

    const item = (fixture.nativeElement as HTMLElement).querySelector(
      '.avatar-menu-item',
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
