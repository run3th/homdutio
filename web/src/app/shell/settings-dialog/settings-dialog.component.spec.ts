import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { DialogRef } from '@angular/cdk/dialog';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import type { ImageCroppedEvent } from 'ngx-image-cropper';

import { SettingsDialogComponent } from './settings-dialog.component';
import { AuthService } from '../../auth/auth.service';
import { ProfileService } from '../../profile/profile.service';

describe('SettingsDialogComponent', () => {
  const displayName = signal<string | null>('Molly Weasley');
  const avatarUrl = signal<string | null>(null);
  let updateProfile: ReturnType<typeof vi.fn>;
  let uploadAvatar: ReturnType<typeof vi.fn>;
  let removeAvatar: ReturnType<typeof vi.fn>;
  let close: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    displayName.set('Molly Weasley');
    avatarUrl.set(null);
    updateProfile = vi.fn((name: string) => of({ id: 'u1', displayName: name, avatarUrl: avatarUrl() }));
    uploadAvatar = vi.fn(() => of({ avatarUrl: '/api/users/u1/avatar?v=1' }));
    removeAvatar = vi.fn(() => of(void 0));
    close = vi.fn();
    TestBed.configureTestingModule({
      imports: [SettingsDialogComponent],
      providers: [
        { provide: AuthService, useValue: { displayName, avatarUrl } },
        { provide: ProfileService, useValue: { updateProfile, uploadAvatar, removeAvatar } },
        { provide: DialogRef, useValue: { close } },
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
});
