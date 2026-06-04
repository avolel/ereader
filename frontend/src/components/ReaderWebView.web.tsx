import { forwardRef, useEffect, useImperativeHandle, useMemo, useRef } from 'react';

import { useThemeContext } from '../providers/ThemeProvider';
import { buildChapterDocument } from '../lib/webviewScripts';
import { HighlightColour, TextAnchor } from '../types';

export type DOMRectLike = { x: number; y: number; width: number; height: number };
export type RenderHighlight = { id: string; anchor: TextAnchor; colour: HighlightColour };
// Scroll-to/flash target: an existing mark by id, or an anchor to resolve.
export type FlashTarget = { id?: string; anchor?: TextAnchor };

export type ReaderWebViewHandle = {
  scrollTo: (y: number) => void;
  applyHighlights: (list: RenderHighlight[]) => void;
  flashTo: (target: FlashTarget) => void;
};

export type ReaderWebViewMessage =
  | { type: 'scroll'; scrollY: number }
  | { type: 'ready'; height: number }
  // anchor has no chapterId — the WebView can't know it; the screen attaches it on save.
  | { type: 'selection'; anchor: Omit<TextAnchor, 'chapterId'>; selectedText: string; rect: DOMRectLike }
  | { type: 'highlightTap'; id: string; rect: DOMRectLike }
  | { type: 'anchorMiss'; id: string };

type Props = {
  chapterHtml: string;
  assetsBaseUrl: string;
  initialScrollY?: number;
  language?: string | null;
  highlights: RenderHighlight[];
  onMessage: (msg: ReaderWebViewMessage) => void;
};

// Web shim: an iframe driven by srcdoc. window.parent.postMessage carries the
// bridge payloads that ReactNativeWebView uses on native. The cover endpoint
// is auth-gated, but chapter assets are served from the same origin so the
// browser sends the cookie / nothing-special as the iframe loads them.
//
// Caveat: srcdoc iframes have no fetch credentials in some browsers' privacy
// modes — if a downstream EPUB references cross-origin assets we'd need to
// proxy them through the API. For now everything is same-origin so the iframe
// can pull /api/v1/books/{id}/assets/... directly.
const ReaderWebView = forwardRef<ReaderWebViewHandle, Props>(function ReaderWebView(
  { chapterHtml, initialScrollY, language, highlights, onMessage },
  ref,
) {
  const { globalSetting, theme } = useThemeContext();
  const iframeRef = useRef<HTMLIFrameElement | null>(null);

  useImperativeHandle(ref, () => ({
    scrollTo: (y: number) => {
      iframeRef.current?.contentWindow?.scrollTo(0, Number(y) || 0);
    },
    applyHighlights: (list: RenderHighlight[]) => {
      iframeRef.current?.contentWindow?.postMessage({ __er: 'applyHighlights', list }, '*');
    },
    flashTo: (target: FlashTarget) => {
      iframeRef.current?.contentWindow?.postMessage({ __er: 'flashTo', target }, '*');
    },
  }));

  const srcDoc = useMemo(
    () =>
      buildChapterDocument(
        {
          chapterHtml,
          setting: globalSetting,
          webviewColors: theme.colors.webview,
          resolvedMode: theme.resolvedMode,
          language,
          initialScrollY,
        },
        highlights,
      ),
    // `highlights` intentionally excluded: it only seeds first paint. Live
    // add/delete goes through applyHighlights() so the iframe never remounts
    // (preserves scroll). Re-including it would rebuild srcDoc → remount.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [chapterHtml, globalSetting, theme.colors.webview, theme.resolvedMode, language, initialScrollY],
  );

  useEffect(() => {
    function handle(event: MessageEvent) {
      // postMessage from the iframe arrives with source === iframe.contentWindow.
      if (event.source !== iframeRef.current?.contentWindow) return;
      const data = event.data;
      if (!data || typeof data !== 'object') return;
      onMessage(data as ReaderWebViewMessage);
    }
    window.addEventListener('message', handle);
    return () => window.removeEventListener('message', handle);
  }, [onMessage]);

  // Sync highlights to the iframe whenever the prop updates
  useEffect(() => {
    if (iframeRef.current?.contentWindow) {
      iframeRef.current.contentWindow.postMessage(
        { __er: 'applyHighlights', list: highlights },
        '*'
      );
    }
  }, [highlights]);

  return (
    <iframe
      ref={iframeRef}
      srcDoc={srcDoc}
      title="Chapter content"
      style={{
        width: '100%',
        height: '100%',
        border: 0,
        background: theme.colors.webview.background,
      }}
    />
  );
});

export default ReaderWebView;