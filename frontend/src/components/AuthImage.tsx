import { useEffect, useState } from 'react';
import { Image, ImageStyle, Platform, StyleProp, View, ViewStyle } from 'react-native';

import { api } from '../services/api';

type Props = {
  url: string | null;
  style?: StyleProp<ImageStyle>;
  placeholderStyle?: StyleProp<ViewStyle>;
  alt?: string;
};

// Renders an authenticated image. On web we fetch via axios (auth header
// attached by interceptor), turn the blob into an object URL, and use it as
// the <Image> source. On native, <Image> supports request headers directly,
// so we read the token and pass it through.
export default function AuthImage({ url, style, placeholderStyle, alt }: Props) {
  const [src, setSrc] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    let objectUrl: string | null = null;

    if (!url) {
      setSrc(null);
      return;
    }

    async function load() {
      if (!url) return;
      try {
        if (Platform.OS === 'web') {
          const response = await api.get<Blob>(url, { responseType: 'blob' });
          if (cancelled) return;
          objectUrl = URL.createObjectURL(response.data);
          setSrc(objectUrl);
        } else {
          // Native: Image natively supports headers; reading the token here
          // would duplicate interceptor logic. Defer the implementation until
          // native is on the roadmap — for now, show the placeholder.
          setSrc(null);
        }
      } catch {
        if (!cancelled) setSrc(null);
      }
    }

    void load();

    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [url]);

  if (!src) {
    return <View style={[style as StyleProp<ViewStyle>, placeholderStyle]} accessibilityLabel={alt} />;
  }
  return <Image source={{ uri: src }} style={style} accessibilityLabel={alt} />;
}