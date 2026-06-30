import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';

import { avatarColor } from '../../board/tag-color';

/** Avatar diameters (px). The colored-initial glyph and the photo share the same box on each surface. */
const SIZE_PX: Readonly<Record<string, number>> = {
  sm: 21, // 1.3125rem — task-card glyph parity
  md: 36, // 2.25rem — members roster parity
  lg: 64, // join-screen inviter badge
};

/**
 * One component for "show a user's avatar", reused on every surface (cards, comments, members, header/menu,
 * join inviter). Renders the user's photo as an `<img>` when {@link avatarUrl} is set, otherwise the existing
 * deterministic colored-initial glyph ({@link avatarColor} + first letter). If the image fails to load it
 * falls back to the initial too, so a broken/expired URL never shows a missing-image icon.
 *
 * Until Phase 3 wires real avatar URLs everywhere, callers pass only `name` and this always renders the
 * initial — so surfaces can adopt it now and "light up" with photos once URLs arrive, with no further markup
 * change.
 */
@Component({
  selector: 'app-user-avatar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [],
  template: `
    @if (showImage()) {
      <img
        class="user-avatar__img"
        [src]="avatarUrl()"
        [width]="px()"
        [height]="px()"
        [alt]="name() ?? ''"
        (error)="onImageError()"
      />
    } @else {
      <span
        class="user-avatar__initial"
        aria-hidden="true"
        [style.width.px]="px()"
        [style.height.px]="px()"
        [style.fontSize.px]="glyphPx()"
        [style.background]="color()"
        >{{ initial() }}</span
      >
    }
  `,
  styleUrl: './user-avatar.component.scss',
})
export class UserAvatarComponent {
  /** The user's display name — drives the fallback initial and its deterministic color. */
  readonly name = input<string | null>(null);
  /** The user's avatar photo URL, or null/undefined to render the colored initial. */
  readonly avatarUrl = input<string | null | undefined>(null);
  /** Avatar size: a named token (`sm`/`md`/`lg`) or an explicit pixel diameter. */
  readonly size = input<'sm' | 'md' | 'lg' | number>('md');

  /** The avatar URL that failed to load, if any — keyed to the URL so a new (e.g. version-bumped) URL retries. */
  private readonly failedUrl = signal<string | null>(null);

  readonly px = computed(() => {
    const size = this.size();
    return typeof size === 'number' ? size : (SIZE_PX[size] ?? SIZE_PX['md']);
  });

  /** Glyph font size scaled to the box (~0.42× diameter) so a single initial stays centered at any size. */
  readonly glyphPx = computed(() => Math.round(this.px() * 0.42));

  /** Show the photo only when a URL is present and that exact URL hasn't failed to load. */
  readonly showImage = computed(() => {
    const url = this.avatarUrl();
    return !!url && this.failedUrl() !== url;
  });

  readonly color = computed(() => avatarColor(this.name()));

  /** Uppercase first letter of the name, or `?` when unknown (mirrors the cards/members glyph). */
  readonly initial = computed(() => (this.name() ?? '?').charAt(0).toUpperCase() || '?');

  onImageError(): void {
    this.failedUrl.set(this.avatarUrl() ?? null);
  }
}
