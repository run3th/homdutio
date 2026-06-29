import { avatarColor, tagColor } from './tag-color';

describe('tagColor', () => {
  it('maps a known chore name to its fixed color, case-insensitively', () => {
    expect(tagColor('Kitchen')).toBe('#C2703D');
    expect(tagColor('kitchen')).toBe('#C2703D');
    expect(tagColor('  KITCHEN ')).toBe('#C2703D');
  });

  it('is deterministic for an unknown name (same color regardless of casing/spacing)', () => {
    expect(tagColor('zebra')).toBe(tagColor('ZEBRA'));
    expect(tagColor('two words')).toBe(tagColor('  two words  '));
  });

  it('returns a six-digit hex from the palette for unknown names', () => {
    expect(tagColor('something-unusual')).toMatch(/^#[0-9a-f]{6}$/i);
  });

  it('handles null/empty defensively', () => {
    expect(tagColor(null)).toMatch(/^#[0-9a-f]{6}$/i);
    expect(tagColor('')).toMatch(/^#[0-9a-f]{6}$/i);
  });

  it('avatarColor is deterministic and palette-based', () => {
    expect(avatarColor('Molly')).toBe(avatarColor('molly'));
    expect(avatarColor('Arthur')).toMatch(/^#[0-9a-f]{6}$/i);
  });
});
