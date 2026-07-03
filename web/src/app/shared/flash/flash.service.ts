import { Injectable, Injector, inject } from '@angular/core';
import { Overlay } from '@angular/cdk/overlay';
import { ComponentPortal } from '@angular/cdk/portal';

import { FLASH_DATA, FlashComponent, FlashData } from './flash.component';

/**
 * Renders transient, dismissible overlays via CDK `Overlay` (the overlay CSS is already loaded globally).
 * `show` is the plain single-line flash used for assignment feedback (S-03 / push-notifications); `push`
 * (Phase 2) is the notification-styled toast {@link NotificationService.pushNotify} fires. Each overlay owns
 * an auto-dismiss timer inside {@link FlashComponent} and disposes itself; the caller never tracks refs.
 */
@Injectable({ providedIn: 'root' })
export class FlashService {
  private readonly overlay = inject(Overlay);
  private readonly injector = inject(Injector);

  /** Show a plain transient flash message (e.g. "<name> will be notified…"). */
  show(message: string, durationMs = 4500): void {
    this.present({ variant: 'flash', message, durationMs });
  }

  /** Show a push-notification-styled toast with a title + body (Phase 2). */
  push(title: string, body: string, durationMs = 5000): void {
    this.present({ variant: 'push', title, message: body, durationMs });
  }

  private present(data: Omit<FlashData, 'dismiss'>): void {
    const overlayRef = this.overlay.create({
      // Bottom-centre, above the mobile bottom nav; scrolls with nothing (fixed global strategy).
      positionStrategy: this.overlay.position().global().bottom('1.5rem').centerHorizontally(),
      panelClass: 'flash-overlay-panel',
    });

    const portal = new ComponentPortal(
      FlashComponent,
      null,
      Injector.create({
        parent: this.injector,
        providers: [
          { provide: FLASH_DATA, useValue: { ...data, dismiss: () => overlayRef.dispose() } satisfies FlashData },
        ],
      }),
    );
    overlayRef.attach(portal);
  }
}
