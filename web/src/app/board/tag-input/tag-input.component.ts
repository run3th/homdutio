import { Component, computed, forwardRef, input, signal } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

import { tagColor } from '../tag-color';

/** Caps mirrored from the server's `TagNormalization` so the UI rejects before a round-trip. */
const MAX_TAGS = 10;
const MAX_TAG_LENGTH = 50;

/**
 * The S-12 tag chip-input: a from-scratch chip/typeahead control (no Angular Material in this project), wired
 * as a {@link ControlValueAccessor} so a parent form binds it like any `FormControl<string[]>`. Type and press
 * Enter or comma to commit a chip; Backspace on an empty field removes the last; a filtered dropdown of the
 * household's existing tags (fed via the {@link suggestions} input) narrows as you type, with a "Create '…'"
 * row when the typed value isn't already a known tag. Chips carry the same deterministic {@link tagColor} dot
 * the cards use. Normalization (trim + collapse whitespace, case-insensitive de-dup) and the ≤10 / ≤50 caps
 * match the server, so a valid client state always survives the POST/PUT.
 */
@Component({
  selector: 'app-tag-input',
  imports: [],
  templateUrl: './tag-input.component.html',
  styleUrl: './tag-input.component.scss',
  providers: [
    { provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => TagInputComponent), multi: true },
  ],
})
export class TagInputComponent implements ControlValueAccessor {
  /** The household's existing tag values to suggest (signal input so the dropdown reacts when they load). */
  readonly suggestions = input<string[]>([]);

  readonly tagColor = tagColor;
  readonly maxTags = MAX_TAGS;

  /** Selected tags — the control's value. */
  readonly tags = signal<string[]>([]);
  /** The current text in the field (drives filtering + the "Create" row). */
  readonly query = signal('');
  /** Whether the suggestion dropdown is shown (open on focus, closed on blur/Escape). */
  readonly open = signal(false);
  readonly disabled = signal(false);
  /** A transient client-side validation message (over-length / over-count). */
  readonly error = signal('');

  /** Suggestions not already selected, matching the typed query (case-insensitive), capped for the dropdown. */
  readonly filtered = computed(() => {
    const q = this.clean(this.query()).toLowerCase();
    const selected = new Set(this.tags().map((t) => t.toLowerCase()));
    return this.suggestions()
      .filter((s) => !selected.has(s.toLowerCase()))
      .filter((s) => q === '' || s.toLowerCase().includes(q))
      .slice(0, 8);
  });

  /** Show a "Create '<x>'" row when the typed value is new (not already a tag or an exact suggestion). */
  readonly showCreate = computed(() => {
    const value = this.clean(this.query());
    if (!value || value.length > MAX_TAG_LENGTH) {
      return false;
    }
    const lower = value.toLowerCase();
    const inTags = this.tags().some((t) => t.toLowerCase() === lower);
    const exact = this.suggestions().some((s) => s.toLowerCase() === lower);
    return !inTags && !exact;
  });

  private onChange: (value: string[]) => void = () => {};
  private onTouched: () => void = () => {};

  // --- ControlValueAccessor ------------------------------------------------------------------------

  writeValue(value: string[] | null): void {
    this.tags.set(value ?? []);
  }

  registerOnChange(fn: (value: string[]) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled.set(isDisabled);
  }

  // --- Interaction ---------------------------------------------------------------------------------

  /** Commit a tag (typed text or a chosen suggestion), applying the same rules the server enforces. */
  addTag(raw: string): void {
    const value = this.clean(raw);
    if (!value) {
      return;
    }
    if (value.length > MAX_TAG_LENGTH) {
      this.error.set(`Tags can be at most ${MAX_TAG_LENGTH} characters.`);
      return;
    }
    if (this.tags().some((t) => t.toLowerCase() === value.toLowerCase())) {
      this.query.set(''); // already present (case-insensitive) — just clear the field
      return;
    }
    if (this.tags().length >= MAX_TAGS) {
      this.error.set(`A task can have at most ${MAX_TAGS} tags.`);
      return;
    }
    this.tags.update((list) => [...list, value]);
    this.query.set('');
    this.error.set('');
    this.onChange(this.tags());
  }

  removeTag(index: number): void {
    this.tags.update((list) => list.filter((_, i) => i !== index));
    this.error.set('');
    this.onChange(this.tags());
  }

  onInput(event: Event): void {
    this.query.set((event.target as HTMLInputElement).value);
    this.error.set('');
    this.open.set(true);
  }

  onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' || event.key === ',') {
      if (this.query().trim()) {
        event.preventDefault();
        this.addTag(this.query());
      }
    } else if (event.key === 'Backspace' && this.query() === '' && this.tags().length > 0) {
      this.removeTag(this.tags().length - 1);
    } else if (event.key === 'Escape') {
      this.open.set(false);
    }
  }

  onBlur(): void {
    this.open.set(false);
    this.onTouched();
  }

  private clean(value: string): string {
    return value.trim().replace(/\s+/g, ' ');
  }
}
