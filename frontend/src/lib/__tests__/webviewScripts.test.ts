import { buildChapterDocument } from '../webviewScripts';
import { ReadingSetting } from '../../types';

// Minimal reading setting — only the typography fields the envelope reads.
const setting: ReadingSetting = {
  bookId: null,
  theme: 'light',
  fontFamily: 'serif',
  fontSize: 16,
  lineSpacing: 1.5,
  marginHorizontal: 40,
  marginVertical: 20,
  lastChapterId: null,
  lastScrollOffset: 0,
  lastReadAt: null,
  updatedAt: '2026-01-01T00:00:00Z',
};

const webviewColors = {
  background: '#fff',
  foreground: '#111',
  link: '#06f',
  selection: 'rgba(0,0,0,0.2)',
};

function build(highlights: Parameters<typeof buildChapterDocument>[1] = []) {
  return buildChapterDocument(
    { chapterHtml: '<p>Hello world</p>', setting, webviewColors, resolvedMode: 'light' },
    highlights,
  );
}

describe('buildChapterDocument accessibility', () => {
  it('Should_MarkContentAsDocumentLandmark_When_Built', () => {
    const html = build();
    expect(html).toContain('id="er-content"');
    expect(html).toContain('role="document"');
    expect(html).toContain('aria-label="Chapter content"');
    // Programmatically focusable so the reader can focus it on chapter change.
    expect(html).toContain('tabindex="-1"');
  });

  it('Should_SetHtmlLang_When_LanguageProvided', () => {
    const html = buildChapterDocument(
      { chapterHtml: '<p>x</p>', setting, webviewColors, resolvedMode: 'light', language: 'fr' },
      [],
    );
    expect(html).toContain('<html lang="fr"');
  });

  it('Should_IncludeReducedMotionGuard_When_Built', () => {
    const html = build();
    expect(html).toContain('prefers-reduced-motion: reduce');
    expect(html).toContain('.er-anchor-flash { animation: none; }');
  });

  it('Should_ExposeFocusContentBridge_When_Built', () => {
    const html = build();
    // Used by ReaderWebView.focusContent() to move AT focus into the new chapter.
    expect(html).toContain('__erFocusContent');
    expect(html).toContain("d.__er === 'focusContent'");
  });

  it('Should_CaptureKeyboardSelection_When_Built', () => {
    const html = build();
    // Shift+Arrow selection emits via keyup; Enter/Space activates a highlight.
    expect(html).toContain("addEventListener('keyup'");
    expect(html).toContain("addEventListener('keydown'");
  });

  it('Should_LabelHighlightsForScreenReaders_When_Built', () => {
    const html = build([
      {
        id: 'h1',
        anchor: { chapterId: 'c', start: 0, end: 5, prefix: '', exact: 'Hello', suffix: '' },
        colour: 'yellow',
        hasNote: true,
      },
    ]);
    // The generated mark is keyboard-focusable and named with colour + note state.
    expect(html).toContain("setAttribute('tabindex', '0')");
    expect(html).toContain("colour + ' highlight'");
    expect(html).toContain(", has note");
    // Seed carries the highlight (with its hasNote flag) into first paint.
    expect(html).toContain('"id":"h1"');
    expect(html).toContain('"hasNote":true');
  });
});
