// Pure offset/anchor arithmetic for text highlights — DOM-free so it can be
// unit-tested off-DOM. This is the source of truth for the trusted-vs-reflow
// resolution logic. The injected WebView script in webviewScripts.ts mirrors
// these two functions (it runs in the iframe/WebView with no module system, so
// it can't import this); keep the two in sync.

import { TextAnchor } from '../types';

// Characters of surrounding context stored on each anchor. Used to re-find the
// quote after the document reflows and the raw offsets drift.
export const ANCHOR_CONTEXT_LEN = 32;

export type AnchorOffsets = Pick<TextAnchor, 'start' | 'end' | 'prefix' | 'exact' | 'suffix'>;

// Build the reflow-safe selector fields from a [start, end) range over `text`.
export function makeAnchorFields(text: string, start: number, end: number): AnchorOffsets {
  return {
    start,
    end,
    exact: text.slice(start, end),
    prefix: text.slice(Math.max(0, start - ANCHOR_CONTEXT_LEN), start),
    suffix: text.slice(end, end + ANCHOR_CONTEXT_LEN),
  };
}

// Resolve an anchor back to concrete offsets in the current `text`.
// 1. Trust the stored offsets if they still slice out the expected `exact`.
// 2. Otherwise (reflow drift) re-find by the quote.
// 3. Give up → null (caller treats as an orphaned/missed anchor).
export function resolveAnchorOffsets(
  text: string,
  anchor: Pick<TextAnchor, 'start' | 'end' | 'exact'>,
): { start: number; end: number } | null {
  const { start, end, exact } = anchor;
  const trusted =
    typeof start === 'number' &&
    typeof end === 'number' &&
    start >= 0 &&
    end <= text.length &&
    start < end &&
    (!exact || text.slice(start, end) === exact);
  if (trusted) return { start, end };

  if (exact) {
    const idx = text.indexOf(exact);
    if (idx >= 0) return { start: idx, end: idx + exact.length };
  }
  return null;
}
