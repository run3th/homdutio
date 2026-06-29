/**
 * Deterministic, client-side colors for tag/category chips and board avatars — ported from the mockup's
 * `tagColor` logic so the UI matches the design without any backend/schema involvement. A small known-name
 * map covers common chores; everything else hashes its lowercased name to a fixed palette so the same name
 * always renders the same color. Reused by the task card (Phase 3) and the tag-input/chips (Phase 6).
 */
const KNOWN: Readonly<Record<string, string>> = {
  kitchen: '#C2703D',
  cleaning: '#2F6B8F',
  garden: '#3A7D52',
  pets: '#7A5AA6',
  shopping: '#B5852F',
  laundry: '#5B5FA6',
};

const PALETTE: readonly string[] = [
  '#2F6B8F',
  '#3A7D52',
  '#C2703D',
  '#7A5AA6',
  '#B5852F',
  '#5B5FA6',
  '#2C6E63',
  '#B5524A',
  '#4F6B8F',
  '#3E7C8A',
];

/** Stable index into PALETTE for a key (mockup hash: `h = (h * 31 + charCode) >>> 0`). */
function paletteIndex(key: string): number {
  let h = 0;
  for (let i = 0; i < key.length; i++) {
    h = (h * 31 + key.charCodeAt(i)) >>> 0;
  }
  return h % PALETTE.length;
}

/** Color for a tag/category chip dot: known-name override, else a hashed palette color. */
export function tagColor(name: string | null | undefined): string {
  const key = (name ?? '').trim().toLowerCase();
  return KNOWN[key] ?? PALETTE[paletteIndex(key)];
}

/**
 * Color for a board avatar. The board carries only member display names (no per-member color), so derive
 * a stable color from the name the same way — consistent across a session without extra server data.
 */
export function avatarColor(name: string | null | undefined): string {
  const key = (name ?? '').trim().toLowerCase();
  return PALETTE[paletteIndex(key)];
}
