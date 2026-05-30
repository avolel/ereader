import { forwardRef, useImperativeHandle, useRef } from 'react';
import { StyleSheet } from 'react-native';
import { WebView, WebViewMessageEvent } from 'react-native-webview';

import { useThemeContext } from '../providers/ThemeProvider';
import { buildChapterDocument } from '../lib/webviewScripts';

export type ReaderWebViewHandle = {
  scrollTo: (y: number) => void;
};

export type ReaderWebViewMessage =
  | { type: 'scroll'; scrollY: number }
  | { type: 'ready'; height: number };

type Props = {
  chapterHtml: string;
  assetsBaseUrl: string;
  initialScrollY?: number;
  language?: string | null;
  onMessage: (msg: ReaderWebViewMessage) => void;
};

// Native: react-native-webview. Re-mounting on every theme change would lose
// scroll position — instead, we re-inject a small JS snippet so the CSS
// variables and document scroll update in place.
const ReaderWebView = forwardRef<ReaderWebViewHandle, Props>(function ReaderWebView(
  { chapterHtml, assetsBaseUrl, initialScrollY, language, onMessage },
  ref,
) {
  const { globalSetting, theme } = useThemeContext();
  const webRef = useRef<WebView>(null);

  useImperativeHandle(ref, () => ({
    scrollTo: (y: number) => {
      webRef.current?.injectJavaScript(`window.scrollTo(0, ${Number(y) || 0}); true;`);
    },
  }));

  const html = buildChapterDocument({
    chapterHtml,
    setting: globalSetting,
    webviewColors: theme.colors.webview,
    resolvedMode: theme.resolvedMode,
    language,
    initialScrollY,
  });

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