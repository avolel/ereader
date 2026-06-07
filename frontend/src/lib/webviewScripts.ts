import { ThemeColors } from '../theme/tokens';
import { HighlightColour, ReadingSetting, TextAnchor } from '../types';

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

// `hasNote` drives the screen-reader "has note" suffix on the mark's label; it's
// optional so callers that don't track notes still produce valid highlights.
type HighlightSeed = {
  id: string;
  anchor: TextAnchor;
  colour: HighlightColour;
  hasNote?: boolean;
};

export function buildChapterDocument(args: BuildChapterDocumentArgs,
  highlights: HighlightSeed[]): string {
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
  /* Respect reduced-motion: drop the flash animation entirely (WCAG 2.3.3). */
  @media (prefers-reduced-motion: reduce) {
    .er-anchor-flash { animation: none; }
  }
  /* #er-content is programmatically focusable (focused on chapter change) but
     shouldn't show a focus ring; keyboard-focusable highlights should. */
  #er-content:focus { outline: none; }
  mark.er-hl:focus-visible { outline: 2px solid var(--er-link); outline-offset: 1px; }

  /* Saved highlights. Semi-transparent so dark-theme text stays legible. */
  mark.er-hl { color: inherit; border-radius: 2px; cursor: pointer; }
  mark.er-hl[data-colour="yellow"] { background: rgba(255, 214, 0, .40); }
  mark.er-hl[data-colour="green"]  { background: rgba( 76, 217, 100, .38); }
  mark.er-hl[data-colour="blue"]   { background: rgba( 90, 200, 250, .38); }
  mark.er-hl[data-colour="pink"]   { background: rgba(255, 105, 180, .36); }
  mark.er-hl[data-colour="orange"] { background: rgba(255, 149,   0, .38); }
</style>
</head>
<body>
<div id="er-content" role="document" aria-label="Chapter content" tabindex="-1">${chapterHtml}</div>
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
${buildAnnotationScript(highlights)}
</body>
</html>`;
}

// Native-only: a WebView can't attach an Authorization header to subresource
// requests (in-chapter <img>, linked stylesheets), and absolute-path asset URLs
// like `/api/v1/books/{id}/assets/...` resolve against the WebView base URL's
// *origin only* — so a token on the base URL's query string would be dropped.
// We therefore append `?access_token=` to each in-app asset URL in the chapter
// HTML itself. The backend honours this query token only on the media GET routes
// (see MediaQueryToken.cs). No-op without a token. The web shim is a separate
// file and never calls this (its iframe loads assets same-origin).
//
// Known gap: assets referenced from *within* a linked CSS file (e.g.
// `background: url(...)`) aren't rewritten here and would still 401. EPUBs rarely
// rely on those; Phase B's signed-URL transport (B1) removes the limitation.
export function withAssetToken(chapterHtml: string, token: string | null): string {
  if (!token) return chapterHtml;
  const encoded = encodeURIComponent(token);
  return chapterHtml.replace(
    /(\b(?:src|href)\s*=\s*["'])(\/api\/v1\/books\/[^"']*?\/assets\/[^"']*?)(["'])/gi,
    (_match, prefix: string, assetUrl: string, quote: string) =>
      `${prefix}${appendQueryParam(assetUrl, 'access_token', encoded)}${quote}`,
  );
}

// Insert `key=value` into a URL's query string, keeping any existing query and
// preserving a trailing #fragment (so e.g. `foo.svg#icon` stays addressable).
function appendQueryParam(url: string, key: string, value: string): string {
  const hashIndex = url.indexOf('#');
  const path = hashIndex >= 0 ? url.slice(0, hashIndex) : url;
  const fragment = hashIndex >= 0 ? url.slice(hashIndex) : '';
  const separator = path.includes('?') ? '&' : '?';
  return `${path}${separator}${key}=${value}${fragment}`;
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

function buildAnnotationScript(highlights: HighlightSeed[]): string {
  // Escape the seed so an `</script>` inside any `exact`/`prefix`/`suffix`
  // can't terminate this script tag early.
  const seed = JSON.stringify(highlights).replace(/</g, '\\u003c');

  return `<script>
    (function() {
      var ROOT_ID = 'er-content';
      function root() { return document.getElementById(ROOT_ID); }

      // Own copy of the bridge sender — the scroll IIFE's send() is out of scope here.
      function send(msg) {
        try {
          if (window.ReactNativeWebView && window.ReactNativeWebView.postMessage) {
            window.ReactNativeWebView.postMessage(JSON.stringify(msg));
          } else if (window.parent && window.parent !== window) {
            window.parent.postMessage(msg, '*');
          }
        } catch (e) { /* noop */ }
      }

      function rectOf(r) {
        return { x: r.left, y: r.top, width: r.width, height: r.height };
      }

      // --- offset <-> DOM mapping over #er-content text nodes -------------------
      function textNodes() {
        var r = root();
        if (!r) return [];
        var walker = document.createTreeWalker(r, NodeFilter.SHOW_TEXT, null);
        var nodes = [], n;
        while ((n = walker.nextNode())) nodes.push(n);
        return nodes;
      }
      function fullText() { var r = root(); return r ? r.textContent : ''; }

      function offsetOf(node, nodeOffset) {
        var nodes = textNodes(), total = 0;
        for (var i = 0; i < nodes.length; i++) {
          if (nodes[i] === node) return total + nodeOffset;
          total += nodes[i].nodeValue.length;
        }
        return -1;
      }

      // Text-node segments overlapping [start, end), each fully inside one node so
      // surroundContents() is always safe (it only ever splits a single text node).
      function collectSegments(start, end) {
        var nodes = textNodes(), total = 0, segs = [];
        for (var i = 0; i < nodes.length; i++) {
          var len = nodes[i].nodeValue.length;
          var from = Math.max(start, total), to = Math.min(end, total + len);
          if (from < to) segs.push({ node: nodes[i], from: from - total, to: to - total });
          total += len;
        }
        return segs;
      }

      function wrapRange(start, end, id, colour, hasNote) {
        var segs = collectSegments(start, end);
        if (!segs.length) return false;
        var label = colour + ' highlight' + (hasNote ? ', has note' : '');
        segs.forEach(function(seg) {
          var range = document.createRange();
          range.setStart(seg.node, seg.from);
          range.setEnd(seg.node, seg.to);
          var mark = document.createElement('mark');
          mark.className = 'er-hl';
          mark.setAttribute('data-id', id);
          mark.setAttribute('data-colour', colour);
          // Keyboard-focusable + named so AT announces "<colour> highlight[, has note]".
          mark.setAttribute('tabindex', '0');
          mark.setAttribute('aria-label', label);
          range.surroundContents(mark);
        });
        return true;
      }

      function clearHighlights() {
        var r = root();
        if (!r) return;
        var marks = r.querySelectorAll('mark.er-hl');
        for (var i = 0; i < marks.length; i++) {
          var m = marks[i], parent = m.parentNode;
          while (m.firstChild) parent.insertBefore(m.firstChild, m);
          parent.removeChild(m);
          parent.normalize(); // re-merge split text nodes so offsets stay stable
        }
      }

      // Resolve an anchor to concrete [start,end) offsets in the current text.
      // Mirrors resolveAnchorOffsets() in lib/anchors.ts — keep the two in sync.
      function resolveOffsets(text, a) {
        a = a || {};
        var start = a.start, end = a.end;
        var trusted =
          typeof start === 'number' && typeof end === 'number' &&
          start >= 0 && end <= text.length && start < end &&
          (!a.exact || text.slice(start, end) === a.exact);
        if (trusted) return { start: start, end: end };
        if (a.exact) {
          var idx = text.indexOf(a.exact);
          if (idx >= 0) return { start: idx, end: idx + a.exact.length };
        }
        return null;
      }

      // --- public: re-render the highlight layer -------------------------------
      window.__erApplyHighlights = function(list) {
        clearHighlights();
        var text = fullText();
        (list || []).forEach(function(h) {
          var off = resolveOffsets(text, h.anchor);
          var ok = off ? wrapRange(off.start, off.end, h.id, h.colour, h.hasNote) : false;
          if (!ok) send({ type: 'anchorMiss', id: h.id });
        });
      };

      // --- public: scroll to + briefly flash an annotation/bookmark ------------
      // Prefer an already-rendered mark (by id); otherwise resolve the anchor and
      // flash a transient wrapper. Chapter-top bookmarks (no range) just scroll up.
      window.__erFlashTo = function(target) {
        target = target || {};
        var r = root();
        var el = (target.id && r) ? r.querySelector('mark.er-hl[data-id="' + target.id + '"]') : null;
        if (el) {
          el.scrollIntoView({ block: 'center' });
          el.classList.add('er-anchor-flash');
          setTimeout(function() { el.classList.remove('er-anchor-flash'); }, 1700);
          return;
        }
        if (target.anchor) {
          var off = resolveOffsets(fullText(), target.anchor);
          if (off && off.start < off.end) {
            var segs = collectSegments(off.start, off.end);
            if (segs.length) {
              var seg = segs[0], range = document.createRange();
              range.setStart(seg.node, seg.from);
              range.setEnd(seg.node, seg.to);
              var span = document.createElement('span');
              range.surroundContents(span);
              span.scrollIntoView({ block: 'center' });
              span.classList.add('er-anchor-flash');
              setTimeout(function() {
                var parent = span.parentNode;
                while (span.firstChild) parent.insertBefore(span.firstChild, span);
                parent.removeChild(span);
                parent.normalize();
              }, 1700);
              return;
            }
          }
          window.scrollTo(0, 0);
        }
      };

      // --- public: move focus into the chapter (called on chapter change) ------
      // Focuses the #er-content container so screen-reader/keyboard focus lands at
      // the top of the new chapter rather than wherever it was in the chrome.
      window.__erFocusContent = function() {
        var r = root();
        if (r && typeof r.focus === 'function') r.focus();
      };

      // Web (iframe) bridge: native calls window.__er* directly via injectJavaScript,
      // but the web shim drives the iframe through postMessage — handle it here.
      window.addEventListener('message', function(e) {
        var d = e.data;
        if (!d || typeof d !== 'object') return;
        if (d.__er === 'applyHighlights') window.__erApplyHighlights(d.list);
        else if (d.__er === 'flashTo') window.__erFlashTo(d.target);
        else if (d.__er === 'focusContent') window.__erFocusContent();
      });

      // --- selection capture ----------------------------------------------------
      function captureSelection() {
        var sel = window.getSelection();
        if (!sel || sel.isCollapsed || sel.rangeCount === 0) return;
        var range = sel.getRangeAt(0), r = root();
        if (!r || !r.contains(range.startContainer) || !r.contains(range.endContainer)) return;
        // Only handle text-node endpoints; element endpoints are rare and skipped.
        if (range.startContainer.nodeType !== 3 || range.endContainer.nodeType !== 3) return;
        var start = offsetOf(range.startContainer, range.startOffset);
        var end = offsetOf(range.endContainer, range.endOffset);
        if (start < 0 || end < 0 || start >= end) return;
        var text = fullText(), exact = sel.toString();
        send({
          type: 'selection',
          anchor: {
            start: start, end: end, exact: exact,
            prefix: text.slice(Math.max(0, start - 32), start),
            suffix: text.slice(end, end + 32)
          },
          selectedText: exact,
          rect: rectOf(range.getBoundingClientRect())
        });
      }
      document.addEventListener('mouseup', captureSelection);
      document.addEventListener('touchend', captureSelection);
      // Keyboard-driven selection (Shift+Arrow) never fires mouseup/touchend, so
      // capture on keyup while Shift is held — required for highlight-by-keyboard
      // (FR-29). captureSelection() self-guards on collapsed/out-of-root ranges.
      document.addEventListener('keyup', function(e) {
        if (e.shiftKey) captureSelection();
      });

      // --- tap or key-activate an existing highlight ---------------------------
      function emitHighlightTap(mark) {
        send({
          type: 'highlightTap',
          id: mark.getAttribute('data-id'),
          rect: rectOf(mark.getBoundingClientRect())
        });
      }
      document.addEventListener('click', function(e) {
        var mark = e.target && e.target.closest ? e.target.closest('mark.er-hl') : null;
        if (mark) emitHighlightTap(mark);
      });
      // Enter/Space activate a focused highlight, mirroring click (FR-29).
      document.addEventListener('keydown', function(e) {
        if (e.key !== 'Enter' && e.key !== ' ' && e.key !== 'Spacebar') return;
        var mark = e.target && e.target.closest ? e.target.closest('mark.er-hl') : null;
        if (!mark) return;
        e.preventDefault();
        emitHighlightTap(mark);
      });

      // --- seed first paint with saved highlights ------------------------------
      function applySeed() { window.__erApplyHighlights(${seed}); }
      if (document.readyState === 'complete') applySeed();
      else window.addEventListener('load', applySeed);
    })();
    </script>`;
}