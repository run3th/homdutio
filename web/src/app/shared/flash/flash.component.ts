import { Component, InjectionToken, OnDestroy, OnInit, inject, signal } from '@angular/core';

import { PushCardComponent } from '../../notifications/push-card/push-card.component';

/**
 * The two visual shapes a transient overlay takes:
 * - `flash` — a plain single-line confirmation (Phase 1: assignment feedback).
 * - `push` — a notification-styled toast with a title + body (Phase 2: {@link NotificationService.pushNotify}).
 */
export type FlashVariant = 'flash' | 'push';

/** The payload {@link FlashService} injects into a {@link FlashComponent} instance via CDK portal. */
export interface FlashData {
  variant: FlashVariant;
  /** Bold heading — used by the `push` variant; omitted for a plain `flash`. */
  title?: string;
  /** The message body (the whole content for a `flash`, the sub-line for a `push`). */
  message: string;
  /** Auto-dismiss delay in ms; the caller tears the overlay down when it fires (or on manual dismiss). */
  durationMs: number;
  /** Disposes the host overlay — called on auto-dismiss timeout or the close button. */
  dismiss: () => void;
}

/** The DI token carrying the per-instance {@link FlashData} into the portal-attached component. */
export const FLASH_DATA = new InjectionToken<FlashData>('FLASH_DATA');

/**
 * The transient overlay body rendered by {@link FlashService}. Owns its own auto-dismiss timer (cleared on
 * destroy so a manual dismiss never double-fires) and calls back to dispose the host overlay. Styled as a
 * plain flash or a push-notification card by {@link FlashData.variant}.
 */
@Component({
  selector: 'app-flash',
  imports: [PushCardComponent],
  templateUrl: './flash.component.html',
  styleUrl: './flash.component.scss',
})
export class FlashComponent implements OnInit, OnDestroy {
  readonly data = inject(FLASH_DATA);
  /** Mirrors {@link FlashData} into a signal-friendly shape for the template. */
  readonly leaving = signal(false);

  private timer?: ReturnType<typeof setTimeout>;

  ngOnInit(): void {
    this.timer = setTimeout(() => this.data.dismiss(), this.data.durationMs);
  }

  ngOnDestroy(): void {
    if (this.timer !== undefined) {
      clearTimeout(this.timer);
    }
  }

  dismiss(): void {
    this.data.dismiss();
  }
}
