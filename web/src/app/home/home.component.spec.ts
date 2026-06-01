import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideRouter, Router } from '@angular/router';

import { HomeComponent } from './home.component';
import { AuthService } from '../auth/auth.service';

describe('HomeComponent', () => {
  let logout: ReturnType<typeof vi.fn>;
  let navSpy: ReturnType<typeof vi.spyOn>;
  const email = signal<string | null>('user@example.com');

  beforeEach(() => {
    logout = vi.fn();
    TestBed.configureTestingModule({
      imports: [HomeComponent],
      providers: [provideRouter([]), { provide: AuthService, useValue: { email, logout } }],
    });
    navSpy = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  });

  it('shows the signed-in email', () => {
    const fixture = TestBed.createComponent(HomeComponent);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('user@example.com');
  });

  it('logout clears auth state and navigates to /login', () => {
    const fixture = TestBed.createComponent(HomeComponent);

    fixture.componentInstance.logout();

    expect(logout).toHaveBeenCalledOnce();
    expect(navSpy).toHaveBeenCalledWith(['/login']);
  });
});
