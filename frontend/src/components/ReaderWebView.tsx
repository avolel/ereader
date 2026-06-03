import { forwardRef, useImperativeHandle, useMemo, useRef } from 'react';
import { StyleSheet } from 'react-native';
import { WebView, WebViewMessageEvent } from 'react-native-webview';

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

// Native: react-native-webview. Re-mounting on every theme change would lose
// scroll position — instead, we re-inject a small JS snippet so the CSS
// variables and document scroll update in place.
const ReaderWebView = forwardRef<ReaderWebViewHandle, Props>(function ReaderWebView(
  { chapterHtml, assetsBaseUrl, initialScrollY, language, highlights, onMessage },
  ref,
) {
  const { globalSetting, theme } = useThemeContext();
  const webRef = useRef<WebView>(null);

  useImperativeHandle(ref, () => ({
    scrollTo: (y: number) => {
      webRef.current?.injectJavaScript(`window.scrollTo(0, ${Number(y) || 0}); true;`);
    },
    applyHighlights: (list: RenderHighlight[]) => {
      webRef.current?.injectJavaScript(`window.__erApplyHighlights(${JSON.stringify(list)}); true;`);
    },
    flashTo: (target: FlashTarget) => {
      webRef.current?.injectJavaScript(`window.__erFlashTo(${JSON.stringify(target)}); true;`);
    },
  }));

  const html = useMemo(
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
    // highlights seeds first paint only; live updates re-inject via applyHighlights.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [chapterHtml, globalSetting, theme.colors.webview, theme.resolvedMode, language, initialScrollY],
  );

  function handleMessage(event: WebViewMessageEvent) {
    try {
      const parsed = JSON.parse(event.nativeEvent.data) as ReaderWebViewMessage;
      onMessage(parsed);
    } catch {
      // Ignore — unknown bridge payloads aren't worth raising.
    }
  }

  return (
    <WebView
      ref={webRef}
      style={styles.webview}
      originWhitelist={['*']}
      source={{ html, baseUrl: assetsBaseUrl }}
      onMessage={handleMessage}
      javaScriptEnabled
      domStorageEnabled={false}
      automaticallyAdjustContentInsets={false}
    />
  );
});

const styles = StyleSheet.create({
  webview: { flex: 1, backgroundColor: 'transparent' },
});

export default ReaderWebView;