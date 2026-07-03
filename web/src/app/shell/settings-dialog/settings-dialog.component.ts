import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { Dialog, DialogRef } from '@angular/cdk/dialog';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { concat, Observable } from 'rxjs';
import { ImageCropperComponent, ImageCroppedEvent } from 'ngx-image-cropper';

import { AuthService } from '../../auth/auth.service';
import { ProfileService } from '../../profile/profile.service';
import { UserAvatarComponent } from '../../shared/user-avatar/user-avatar.component';
import { mapValidationProblem } from '../../auth/validation-problem';
import { NotificationService } from '../../notifications/notification.service';
import { DenyHelpComponent } from '../../notifications/deny-help/deny-help.component';
import { QrComponent } from '../../notifications/qr/qr.component';

/** Display-name length cap — mirrors the backend's `ProfileEndpoints.MaxDisplayNameLength`. */
export const MAX_DISPLAY_NAME_LENGTH = 60;

/** Allowed avatar upload types — mirrors the backend's content-type allow-list. */
const ALLOWED_AVATAR_TYPES = ['image/png', 'image/jpeg'];

/**
 * Fails as `required` when the value is blank once trimmed — Angular's `Validators.required` accepts a
 * whitespace-only string, but the backend trims and rejects it, so guard it client-side too.
 */
function nonBlank(control: AbstractControl): ValidationErrors | null {
  return (control.value ?? '').trim().length > 0 ? null : { required: true };
}

/**
 * The Settings modal (S-09), opened from the avatar menu via `@angular/cdk/dialog`. Edits the display name
 * and the profile photo: pick a file → crop/zoom in a round cropper → the downscaled (~256²) blob is staged,
 * or "Remove photo" stages a removal. Nothing hits the server until **Save changes**, which applies the
 * staged photo change (upload or remove) and then the name in one sequence, so **Cancel** discards
 * everything. {@link ProfileService} pushes the new name/avatar into {@link AuthService} so the header/menu
 * update immediately; other surfaces pick the photo up on their next fetch. Mirrors the reactive-form
 * conventions (signals for `pending`/`errors`, `mapValidationProblem` for 400 bodies).
 */
@Component({
  selector: 'app-settings-dialog',
  imports: [ReactiveFormsModule, ImageCropperComponent, UserAvatarComponent, QrComponent],
  templateUrl: './settings-dialog.component.html',
  styleUrl: './settings-dialog.component.scss',
})
export class SettingsDialogComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly profile = inject(ProfileService);
  private readonly auth = inject(AuthService);
  private readonly dialogRef = inject<DialogRef<void>>(DialogRef);
  private readonly notif = inject(NotificationService);
  private readonly dialog = inject(Dialog);

  readonly maxLength = MAX_DISPLAY_NAME_LENGTH;

  // Notifications management (real-web-push) — a read/act surface, not part of the profile form's Save.
  readonly supported = this.notif.supported;
  readonly isMobile = this.notif.isMobile;
  readonly permission = this.notif.permission;
  readonly notifStatusText = this.notif.notifStatusText;
  readonly deviceList = this.notif.deviceList;
  /** The origin the desktop QR encodes so a phone can open the app and turn notifications on there. */
  readonly appOrigin = location.origin;
  /** Enable is phone-only: a push-capable phone that hasn't granted yet. */
  readonly canEnable = computed(() => this.notif.canActivate && this.notif.permission() !== 'granted');
  /** Mobile PWA nudge: only on a phone that hasn't granted consent yet. */
  readonly showInstallHint = computed(
    () => this.notif.isMobile && this.notif.permission() !== 'granted',
  );
  /** In-flight guard for the enable/remove buttons. */
  readonly notifBusy = signal(false);

  ngOnInit(): void {
    // The device list is server-side; load it whenever Settings opens.
    void this.notif.refreshDevices();
  }

  /** Request real permission and subscribe this browser (real browser prompt). */
  async enableNotifs(): Promise<void> {
    this.notifBusy.set(true);
    try {
      await this.notif.enable();
    } finally {
      this.notifBusy.set(false);
    }
  }

  /** Remove a device from the account registry (unsubscribes locally if it's this browser). */
  async removeDevice(endpoint: string): Promise<void> {
    this.notifBusy.set(true);
    try {
      await this.notif.disable(endpoint);
    } finally {
      this.notifBusy.set(false);
    }
  }

  /** Explain how to re-enable notifications after a browser-level denial. */
  openDenyHelp(): void {
    this.dialog.open(DenyHelpComponent);
  }

  readonly form = this.fb.nonNullable.group({
    displayName: ['', [nonBlank, Validators.maxLength(MAX_DISPLAY_NAME_LENGTH)]],
  });

  /** Mapped validation messages from a 400 (or a generic fallback). */
  readonly errors = signal<string[]>([]);
  readonly pending = signal(false);

  readonly displayName = this.auth.displayName;

  /** The file-input change event that drives the cropper; null when no new file is being cropped. */
  readonly fileEvent = signal<Event | null>(null);
  /** The cropped, downscaled blob awaiting upload on Save; null until the cropper emits one. */
  private readonly croppedBlob = signal<Blob | null>(null);
  /** Set when the user asked to remove their photo; applied (DELETE) on Save. */
  private readonly removeRequested = signal(false);
  /** An avatar-specific error (bad file type) surfaced under the photo controls. */
  readonly avatarError = signal<string | null>(null);

  /** The avatar URL shown in the static preview: the current photo, unless a removal is staged. */
  readonly previewAvatarUrl = computed(() => (this.removeRequested() ? null : this.auth.avatarUrl()));

  /** Whether a "Remove photo" action is offered (there's a current photo or a freshly-cropped one to drop). */
  readonly canRemove = computed(
    () => !this.removeRequested() && (!!this.auth.avatarUrl() || !!this.croppedBlob()),
  );

  constructor() {
    // Prefill with the current display name so the field opens populated, not blank.
    this.form.controls.displayName.setValue(this.auth.displayName() ?? '');
  }

  /** A file was picked: validate its type, then hand the event to the cropper (clearing any staged removal). */
  onFileSelected(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) {
      return;
    }

    if (!ALLOWED_AVATAR_TYPES.includes(file.type)) {
      this.avatarError.set('Please choose a PNG or JPEG image.');
      this.fileEvent.set(null);
      this.croppedBlob.set(null);
      return;
    }

    this.avatarError.set(null);
    this.removeRequested.set(false);
    this.croppedBlob.set(null);
    this.fileEvent.set(event);
  }

  /** The cropper emitted a new crop — stage its blob for upload on Save. */
  onCropped(event: ImageCroppedEvent): void {
    this.croppedBlob.set(event.blob ?? null);
  }

  /** Stage a photo removal (applied on Save); cancels any in-progress crop. */
  requestRemove(): void {
    this.removeRequested.set(true);
    this.fileEvent.set(null);
    this.croppedBlob.set(null);
    this.avatarError.set(null);
  }

  save(): void {
    if (this.form.invalid || this.pending()) {
      this.form.markAllAsTouched();
      return;
    }

    this.errors.set([]);
    this.pending.set(true);

    const displayName = this.form.getRawValue().displayName.trim();

    // Apply the staged photo change first (if any), then the name — in one sequence so a failure surfaces
    // and Save stays open. Cancel (closing without Save) discards every staged change.
    const ops: Observable<unknown>[] = [];
    if (this.removeRequested()) {
      ops.push(this.profile.removeAvatar());
    } else if (this.croppedBlob()) {
      ops.push(this.profile.uploadAvatar(this.croppedBlob()!));
    }
    ops.push(this.profile.updateProfile(displayName));

    concat(...ops).subscribe({
      error: (error: HttpErrorResponse) => {
        this.pending.set(false);
        this.errors.set(
          error.status === 400
            ? mapValidationProblem(error)
            : ['Something went wrong. Please try again.'],
        );
      },
      complete: () => {
        this.pending.set(false);
        this.dialogRef.close();
      },
    });
  }

  close(): void {
    this.dialogRef.close();
  }
}
