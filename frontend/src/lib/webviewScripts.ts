import { ThemeColors } from '../theme/tokens';
import { ReadingSetting } from '../types';

// Build the chapter HTML envelope that the WebView renders. The book's own CSS
// is preserved (assets are served from /api/v1/books/{id}/assets/...), and we
// append a stylesheet AFTER it so our typography wins on body-level rules
// while author-set heading/bold/italic styles remain intact.
//
// scrollHandler script: throttled scroll position reports back to native via
// window.ReactNativeWebView.postMessage. On native this is the WebView bridge;
// on web (iframe) we use window.parent.postMessage.

type BuildChapterDocumentArgs = {
  chapterHtml: string;
  setting: ReadingSetting;
  webviewColors: ThemeColors['webview'];
  resolvedMode: 'light' | 'dark';
  language?: string | null;
  // Scroll the document to this y-pixel after load (reading position restore).
  initialScrollY?: number;
};

export function buildChapterDocument(args: BuildChapterDocumentArgs): string {
  const { chapterHtml, setting, webviewColors, resolvedMode, language, initialScrollY } = args;
  const lang = language ?? 'en';

  return `<!DOCTYPE html>
<html lang="${escapeAttribute(lang)}" data-theme="${resolvedMode}">
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width, initial-scale=1.0" />
<style>
  :root {
    --er-bg: ${webviewColors.background};
    --er-fg: ${webviewColors.foreground};
    --er-link: ${webviewColors.link};
    --er-selection: ${webviewColors.selection};
    --er-font-family: ${cssFontFamily(setting.fontFamily)};
    --er-font-size: ${setting.fontSize}px;
    --er-line-height: ${setting.lineSpacing};
    --er-margin-h: ${setting.marginHorizontal}px;
    --er-margin-v: ${setting.marginVertical}px;
  }
  html, body {
    background: var(--er-bg);
    color: var(--er-fg);
    margin: 0;
    padding: 0;
  }
  body {
    font-family: var(--er-font-family);
    font-size: var(--er-font-size);
    line-height: var(--er-line-height);
    padding: var(--er-margin-v) var(--er-margin-h);
    /* Prevent images from blowing past the viewport on narrow screens */
    word-wrap: break-word;
    overflow-wrap: break-word;
  }
  img, svg, video { max-width: 100%; height: auto; }
  a { color: var(--er-link); }
  ::selection { background: var(--er-selection); }
  /* Highlight flash for search-anchor jumps. */
  .er-anchor-flash { animation: er-flash 1.6s ease-out 1; }
  @keyframes er-flash {
    0%   { background: var(--er-selection); }
    100% { background: transparent; }
  }
</style>
</head>
<body>
${chapterHtml}
<script>
(function() {
  function send(msg) {
    try {
      if (window.ReactNativeWebView && window.ReactNativeWebView.postMessage) {
        window.ReactNativeWebView.postMessage(JSON.stringify(msg));
      } else if (window.parent && window.parent !== window) {
        window.parent.postMessage(msg, '*');
      }
    } catch (e) { /* noop */ }
  }

  // Throttled scroll reporter — emits at most every 400ms while scrolling and
  // once 800ms after the last scroll (trailing edge) so the final resting
  // position is always reported.
  var lastSent = 0;
  var trailingTimer = null;
  function reportScroll() {
    var now = Date.now();
    var y = window.scrollY || window.pageYOffset || 0;
    if (now - lastSent > 400) {
      lastSent = now;
      send({ type: 'scroll', scrollY: y });
    }
    clearTimeout(trailingTimer);
    trailingTimer = setTimeout(function() {
      lastSent = Date.now();
      send({ type: 'scroll', scrollY: window.scrollY || window.pageYOffset || 0 });
    }, 800);
  }
  window.addEventListener('scroll', reportScroll, { passive: true });

  // Restore reading position once images are laid out (otherwise we'd scroll
  // before the chapter has its final height).
  function restoreScroll() {
    var y = ${Number.isFinite(initialScrollY) ? Number(initialScrollY) : 0};
    if (y > 0) window.scrollTo(0, y);
    send({ type: 'ready', height: document.documentElement.scrollHeight });
  }
  if (document.readyState === 'complete') {
    restoreScroll();
  } else {
    window.addEventListener('load', restoreScroll);
  }
})();
</script>
</body>
</html>`;
}

function cssFontFamily(family: string): string {
  switch (family.toLowerCase()) {
    case 'serif':
      return 'Georgia, "Times New Roman", serif';
    case 'sans-serif':
      return '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif';
    case 'monospace':
      return '"SF Mono", Menlo, Consolas, monospace';
    case 'system':
      return 'system-ui, -apple-system, BlinkMacSystemFont, sans-serif';
    default:
      return 'serif';
  }
}

function escapeAttribute(s: string): string {
  return s.replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;');
}