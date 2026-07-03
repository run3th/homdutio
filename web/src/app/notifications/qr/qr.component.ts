import { Component, computed, input } from '@angular/core';

import { qrMatrix } from './qr-code';

/** One dark module's grid coordinate. */
interface QrCell {
  x: number;
  y: number;
}

/**
 * Renders a real, scannable QR code (push-notifications, Settings desktop instructions) as inline SVG built
 * from {@link qrMatrix}. `data` is the URL to encode (the app origin); `size` is the rendered pixel size.
 * The SVG is drawn structurally with Angular bindings (no `innerHTML`/sanitizer bypass) at a `viewBox` in
 * module units so it scales crisply, and the modules fill the box **edge-to-edge** (matching the mockup) —
 * the required light quiet zone is supplied by the white padded frame the caller wraps it in.
 */
@Component({
  selector: 'app-qr',
  imports: [],
  templateUrl: './qr.component.html',
  styleUrl: './qr.component.scss',
})
export class QrComponent {
  /** The text to encode — the app origin URL. */
  readonly data = input.required<string>();
  /** Rendered size in CSS pixels (the SVG scales to this; the module grid drives the resolution). */
  readonly size = input(150);

  private readonly matrix = computed(() => qrMatrix(this.data()));

  /** Total grid width in modules — the code fills the box edge-to-edge (the frame supplies the quiet zone). */
  readonly total = computed(() => this.matrix().length);

  /** Dark modules as grid coordinates, ready to render as unit rects. */
  readonly cells = computed<QrCell[]>(() => {
    const out: QrCell[] = [];
    this.matrix().forEach((row, r) => {
      row.forEach((dark, c) => {
        if (dark) {
          out.push({ x: c, y: r });
        }
      });
    });
    return out;
  });
}
