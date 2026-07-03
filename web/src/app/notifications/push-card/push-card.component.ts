import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * The notification card's *content* (push-notifications) — the accent app-icon badge, the "HOMDUTIO · now"
 * line, and the title/body. Deliberately container-agnostic (no background/shadow) so the exact same markup
 * renders in two places and stays identical: the Settings **Preview** frame and the live push toast that
 * {@link FlashService.push} delivers. That shared component is the guarantee that "what you preview is what
 * gets sent".
 */
@Component({
  selector: 'app-push-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [],
  templateUrl: './push-card.component.html',
  styleUrl: './push-card.component.scss',
})
export class PushCardComponent {
  readonly title = input<string>('');
  readonly body = input<string>('');
}
