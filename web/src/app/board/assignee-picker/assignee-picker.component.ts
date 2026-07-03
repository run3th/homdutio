import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

import { Member } from '../../household/member.service';
import { avatarColor } from '../tag-color';

/** One rendered chip: the leading "Anyone" (empty id, no initial) or a member (colored avatar + initial). */
interface AssignChip {
  /** The value emitted on select — '' for "Anyone" (unassigned), else the member's `userId`. */
  id: string;
  name: string;
  /** Uppercase first letter for the avatar; empty for "Anyone" (renders a plain grey dot). */
  initial: string;
  /** Avatar background — the member's deterministic colour, or grey for "Anyone". */
  color: string;
}

/** Grey used for the "Anyone" avatar (matches the mockup's neutral chip). */
const ANYONE_COLOR = '#c7ccd4';

/**
 * The admin-only "Assign to" picker (push-notifications), rendered as the mockup's chip row: a leading
 * "Anyone" (unassigned) plus one chip per member (colored avatar + initial + name, `isSelf` → " (you)").
 * Presentational + value-bound — the parent form owns the state: `value` is the selected id ('' = Anyone),
 * `valueChange` fires on click. Replaces the earlier native `<select>` to match the reference design.
 */
@Component({
  selector: 'app-assignee-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [],
  templateUrl: './assignee-picker.component.html',
  styleUrl: './assignee-picker.component.scss',
})
export class AssigneePickerComponent {
  /** The household roster to build member chips from. */
  readonly members = input<Member[]>([]);
  /** The currently selected id: '' for "Anyone", else a member `userId`. */
  readonly value = input<string>('');
  /** Emits the newly selected id when a chip is clicked. */
  readonly valueChange = output<string>();

  readonly chips = computed<AssignChip[]>(() => [
    { id: '', name: 'Anyone', initial: '', color: ANYONE_COLOR },
    ...this.members().map((m) => ({
      id: m.userId,
      name: m.isSelf ? `${m.displayName} (you)` : m.displayName,
      initial: m.displayName.charAt(0).toUpperCase(),
      color: avatarColor(m.displayName),
    })),
  ]);

  select(id: string): void {
    this.valueChange.emit(id);
  }
}
