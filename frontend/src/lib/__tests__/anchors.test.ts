import { makeAnchorFields, resolveAnchorOffsets } from '../anchors';

const TEXT =
  'The quick brown fox jumps over the lazy dog. ' +
  'Pack my box with five dozen liquor jugs.';

describe('anchors offset round-trip', () => {
  it('Should_ResolveToSameOffsets_When_TextUnchanged', () => {
    const start = TEXT.indexOf('brown fox');
    const end = start + 'brown fox'.length;

    const anchor = makeAnchorFields(TEXT, start, end);
    expect(anchor.exact).toBe('brown fox');

    const resolved = resolveAnchorOffsets(TEXT, anchor);
    expect(resolved).toEqual({ start, end });
  });

  it('Should_ReFindByQuote_When_OffsetsDriftAfterReflow', () => {
    const start = TEXT.indexOf('five dozen');
    const end = start + 'five dozen'.length;
    const anchor = makeAnchorFields(TEXT, start, end);

    // Reflow: a paragraph is prepended, so the stored offsets now point at the
    // wrong place — but the quote is still present and should be re-found.
    const reflowed = 'A new opening paragraph was added.\n\n' + TEXT;
    const newStart = reflowed.indexOf('five dozen');

    const resolved = resolveAnchorOffsets(reflowed, anchor);
    expect(resolved).toEqual({ start: newStart, end: newStart + 'five dozen'.length });
  });

  it('Should_ReturnNull_When_QuoteNoLongerPresent', () => {
    const anchor = makeAnchorFields(TEXT, 4, 9); // "quick"
    const resolved = resolveAnchorOffsets('completely different content', anchor);
    expect(resolved).toBeNull();
  });

  it('Should_DistrustOffsets_When_ExactDoesNotMatchSlice', () => {
    // Offsets are in range and ordered, but the slice no longer equals `exact`,
    // so we must fall back to the quote rather than trust the stale numbers.
    const anchor = { start: 0, end: 5, exact: 'brown' };
    const resolved = resolveAnchorOffsets(TEXT, anchor);
    const idx = TEXT.indexOf('brown');
    expect(resolved).toEqual({ start: idx, end: idx + 'brown'.length });
  });
});
