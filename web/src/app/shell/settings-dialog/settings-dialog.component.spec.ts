import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { DialogRef } from '@angular/cdk/dialog';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import type { ImageCroppedEvent } from 'ngx-image-cropper';

import { SettingsDialogComponent } from './settings-dialog.component';
import { AuthService } from '../../auth/auth.service';
import { ProfileService } from '../../profile/profile.service';
import { NotificationService, NotifDeviceRow, NotifPermission } from '../../notifications/notification.service';

describe('SettingsDialogComponent', () => {
  const displayName = signal<string | null>('Molly Weasley');
  const avatarUrl = signal<string | null>(null);
  let updateProfile: ReturnType<typeof vi.fn>;
  let uploadAvatar: ReturnType<typeof vi.fn>;
  let removeAvatar: ReturnType<typeof vi.fn>;
  let close: ReturnType<typeof vi.fn>;
  let requestNotifs: ReturnType<typeof vi.fn>;
  let pushNotify: ReturnType<typeof vi.fn>;
  let notif: {
    isMobile: ReturnType<typeof signal<boolean>>;
    permission: ReturnType<typeof signal<NotifPermission>>;
    deviceList: ReturnType<typeof signal<NotifDeviceRow[]>>;
    notifStatusText: ReturnType<typeof signal<string>>;
    requestNotifs: typeof requestNotifs;
    pushNotify: typeof pushNotify;
  };

  beforeEach(() => {
    displayName.set('Molly Weasley');
    avatarUrl.set(null);
    updateProfile = vi.fn((name: string) => of({ id: 'u1', displayName: name, avatarUrl: avatarUrl() }));
    uploadAvatar = vi.fn(() => of({ avatarUrl: '/api/users/u1/avatar?v=1' }));
    removeAvatar = vi.fn(() => of(void 0));
    close = vi.fn();
    requestNotifs = vi.fn();
    pushNotify = vi.fn();
    // Desktop by default so the profile-form tests render the (dead-row-free) QR path without a mobile prompt.
    notif = {
      isMobile: signal(false),
      permission: signal<NotifPermission>('default'),
      deviceList: signal<NotifDeviceRow[]>([
        { id: 'nd1', name: 'iPhone Rafała', enabled: false, isCurrent: false, showEnable: false },
      ]),
      notifStatusText: signal("Notifications aren't on for any of your devices yet."),
      requestNotifs,
      pushNotify,
    };
    TestBed.configureTestingModule({
      imports: [SettingsDialogComponent],
      providers: [
        { provide: AuthService, useValue: { displayName, avatarUrl } },
        { provide: ProfileService, useValue: { updateProfile, uploadAvatar, removeAvatar } },
        { provide: DialogRef, useValue: { close } },
        { provide: NotificationService, useValue: notif },
      ],
    });
  });

  function render() {
    const fixture = TestBed.createComponent(SettingsDialogComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('prefills the field with the current display name', () => {
    const fixture = render();
    expect(fixture.componentInstance.form.controls.displayName.value).toBe('Molly Weasley');
  });

  it('saves a trimmed name, updates state via the service, and closes', () => {
    const fixture = render();

    fixture.componentInstance.form.controls.displayName.setValue('  Molly W.  ');
    fixture.componentInstance.save();

    expect(updateProfile).toHaveBeenCalledWith('Molly W.'); // trimmed
    expect(uploadAvatar).not.toHaveBeenCalled();
    expect(removeAvatar).not.toHaveBeenCalled();
    expect(close).toHaveBeenCalled();
  });

  it('does not submit a blank name and shows a validation error', () => {
    const fixture = render();

    fixture.componentInstance.form.controls.displayName.setValue('   ');
    fixture.componentInstance.save();

    expect(updateProfile).not.toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.displayName.touched).toBe(true);
  });

  it('does not submit a too-long name', () => {
    const fixture = render();

    fixture.componentInstance.form.controls.displayName.setValue('x'.repeat(61));
    fixture.componentInstance.save();

    expect(updateProfile).not.toHaveBeenCalled();
    expect(fixture.componentInstance.form.invalid).toBe(true);
  });

  it('uploads a cropped photo then saves the name, and closes', () => {
    const fixture = render();
    const blob = new Blob([new Uint8Array([1, 2, 3])], { type: 'image/png' });

    fixture.componentInstance.onCropped({ blob } as ImageCroppedEvent);
    fixture.componentInstance.save();

    expect(uploadAvatar).toHaveBeenCalledWith(blob);
    expect(updateProfile).toHaveBeenCalled();
    expect(removeAvatar).not.toHaveBeenCalled();
    expect(close).toHaveBeenCalled();
  });

  it('removes the photo then saves the name, and closes', () => {
    avatarUrl.set('/api/users/u1/avatar?v=2'); // a current photo exists to remove
    const fixture = render();

    fixture.componentInstance.requestRemove();
    fixture.componentInstance.save();

    expect(removeAvatar).toHaveBeenCalled();
    expect(uploadAvatar).not.toHaveBeenCalled();
    expect(updateProfile).toHaveBeenCalled();
    expect(close).toHaveBeenCalled();
  });

  it('surfaces validation messages from a 400 and stays open', () => {
    updateProfile.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: { errors: { displayName: ['A display name is required.'] } },
          }),
      ),
    );
    const fixture = render();

    fixture.componentInstance.form.controls.displayName.setValue('Molly');
    fixture.componentInstance.save();

    expect(fixture.componentInstance.errors()).toContain('A display name is required.');
    expect(close).not.toHaveBeenCalled();
    expect(fixture.componentInstance.pending()).toBe(false);
  });

  it('Close dismisses the dialog without saving', () => {
    const fixture = render();
    fixture.componentInstance.close();
    expect(close).toHaveBeenCalled();
    expect(updateProfile).not.toHaveBeenCalled();
  });

  it('desktop: shows the status line + device list, a QR, and no current-device row', () => {
    const el = render().nativeElement as HTMLElement;

    expect(el.textContent).toContain("Notifications aren't on for any of your devices yet.");
    expect(el.querySelector('.notif-device')?.textContent).toContain('iPhone Rafała');
    expect(el.querySelector('app-qr svg')).not.toBeNull();
    expect(el.textContent).toContain('Turn on from your phone');
    // No THIS DEVICE row and no Turn-on button on desktop.
    expect(el.querySelector('.notif-badge')).toBeNull();
    expect(el.querySelector('.notif-enable')).toBeNull();
  });

  it('mobile + not granted: current phone shows THIS DEVICE + Turn on, plus the install hint (no QR)', () => {
    notif.isMobile.set(true);
    notif.deviceList.set([
      { id: 'current', name: 'This phone', enabled: false, isCurrent: true, showEnable: true },
      { id: 'nd1', name: 'iPhone Rafała', enabled: false, isCurrent: false, showEnable: false },
    ]);
    const el = render().nativeElement as HTMLElement;

    expect(el.querySelector('.notif-badge')?.textContent?.trim()).toBe('THIS DEVICE');
    expect(el.textContent).toContain('Install Homdutio');
    expect(el.querySelector('app-qr')).toBeNull();

    (el.querySelector('.notif-enable') as HTMLButtonElement).click();
    expect(requestNotifs).toHaveBeenCalledOnce();
  });

  it('mobile + granted: shows the Preview + "Send a test", which fires the same push content', () => {
    notif.isMobile.set(true);
    notif.permission.set('granted');
    notif.deviceList.set([
      { id: 'current', name: "Rafał's phone", enabled: true, isCurrent: true, showEnable: false },
    ]);
    const el = render().nativeElement as HTMLElement;

    // The preview renders the shared push-card with the exact content the test will send.
    const preview = el.querySelector('.notif-preview-card app-push-card');
    expect(preview?.textContent).toContain('New task assigned to you');
    expect(preview?.textContent).toContain('Kasia assigned you');

    (
      Array.from(el.querySelectorAll('button')).find(
        (b) => b.textContent?.trim() === 'Send a test',
      ) as HTMLButtonElement
    ).click();

    expect(pushNotify).toHaveBeenCalledWith(
      'New task assigned to you',
      'Kasia assigned you “Take out the trash”.',
    );
  });
});
