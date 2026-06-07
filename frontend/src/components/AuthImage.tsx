import { useEffect, useState } from 'react';
import { Image, ImageStyle, Platform, StyleProp, View, ViewStyle } from 'react-native';

import { useAccessToken } from '../hooks/useAccessToken';
import { api } from '../services/api';

type Props = {
  url: string | null;
  style?: StyleProp<ImageStyle>;
  placeholderStyle?: StyleProp<ViewStyle>;
  alt?: string;
};

// Renders an authenticated image. The two platforms can't share one path:
//   - Web: <img>/<Image> can't attach an Authorization header, and there's no
//     SecureStore, so we fetch the bytes via axios (interceptor adds the header),
//     turn the blob into an object URL, and use that as the source.
//   - Native: RN's <Image> accepts request headers directly, so we read the
//     access token and pass it through. (No URL.createObjectURL on native.)
export default function AuthImage({ url, style, placeholderStyle, alt }: Props) {
  const [webSrc, setWebSrc] = useState<string | null>(null);
  const accessToken = useAccessToken();

  useEffect(() => {
    // Web-only: the native branch renders directly from `url` + headers below.
    if (Platform.OS !== 'web') return;

    let cancelled = false;
    let objectUrl: string | null = null;

    if (!url) {
      setWebSrc(null);
      return;
    }

    async function load() {
      if (!url) return;
      try {
        const response = await api.get<Blob>(url, { responseType: 'blob' });
        if (cancelled) return;
        objectUrl = URL.createObjectURL(response.data);
        setWebSrc(objectUrl);
      } catch {
        if (!cancelled) setWebSrc(null);
      }
    }

    void load();

    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [url]);

  const placeholder = (
    <View style={[style as StyleProp<ViewStyle>, placeholderStyle]} accessibilityLabel={alt} />
  );

  if (!url) return placeholder;

  if (Platform.OS !== 'web') {
    // Wait for the token so the very first request carries auth. Unlike the axios
    // interceptor, <Image> won't auto-refresh on a 401 from an expired token —
    // acceptable for covers, which re-render on focus. If this proves flaky,
    // fall back to the ?access_token= query transport used for WebView assets.
    if (!accessToken) return placeholder;
    return (
      <Image
        source={{ uri: url, headers: { Authorization: `Bearer ${accessToken}` } }}
        style={style}
        accessibilityLabel={alt}
      />
    );
  }

  if (!webSrc) return placeholder;
  return <Image source={{ uri: webSrc }} style={style} accessibilityLabel={alt} />;
}
