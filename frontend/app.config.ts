import type { ConfigContext, ExpoConfig } from 'expo/config';

// Dynamic config layered on top of app.json. Expo reads app.json first and passes
// it in as `config`; we only add what has to be computed at build time.
//
// Cleartext HTTP (android.usesCleartextTraffic) is required so dev/preview builds
// can reach the local backend over http:// (the emulator's 10.0.2.2 or a LAN IP).
// Production must talk to an HTTPS backend, so we permit cleartext ONLY for the
// development/preview EAS profiles and when running locally (no profile set).
// EAS sets EAS_BUILD_PROFILE during a build; it's undefined under `expo start`.
const profile = process.env.EAS_BUILD_PROFILE;
const allowCleartext = profile === 'development' || profile === 'preview' || profile === undefined;

export default ({ config }: ConfigContext): ExpoConfig => ({
  ...config,
  // ExpoConfig requires name/slug; app.json always supplies them.
  name: config.name ?? 'EReader',
  slug: config.slug ?? 'ereader',
  plugins: [
    ...(config.plugins ?? []),
    [
      'expo-build-properties',
      {
        android: { usesCleartextTraffic: allowCleartext },
      },
    ],
  ],
});
