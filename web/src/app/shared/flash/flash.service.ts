import { Injectable, Injector, inject } from '@angular/core';
import { Overlay } from '@angular/cdk/overlay';
import { ComponentPortal } from '@angular/cdk/portal';

import { FLASH_DATA, FlashComponent, FlashData } from './flash.component';

/**
 * Renders transient, dismissible overlays via CDK `Overlay` (the overlay CSS is already loaded globally).
 * `show` is the plain single-line flash used for assignment feedback (e.g. "<name> will be notified…").
 * Each overlay owns an auto-dismiss timer inside {@link FlashComponent} and disposes itself; the caller
 * never tracks refs. (Real push delivery is server-side now — there is no in-app push toast.)
 */
@Injectable({ providedIn: 'root' })
export class FlashService {
  private readonly overlay = inject(Overlay);
  private readonly injector = inject(Injector);

  /** Show a plain transient flash message (e.g. "<name> will be notified…"). */
  show(message: string, durationMs = 4500): void {
    this.present({ message, durationMs });
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
