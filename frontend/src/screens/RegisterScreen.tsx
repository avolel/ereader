import { Link, router } from 'expo-router';
import { useState } from 'react';
import {
  ActivityIndicator,
  StyleSheet,
  Text,
  TextInput,
  View,
} from 'react-native';

import IconButton from '../components/a11y/IconButton';
import { useAnnouncer } from '../components/a11y/useAnnouncer';
import { useAuth } from '../providers/AuthProvider';
import { useTheme } from '../providers/ThemeProvider';
import { extractApiError } from '../services/errors';

export default function RegisterScreen() {
  const { register } = useAuth();
  const theme = useTheme();
  const { announce } = useAnnouncer();
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  async function onSubmit() {
    if (submitting) return;
    setSubmitting(true);
    setError(null);
    try {
      await register(username, password);
      router.replace('/library');
    } catch (err) {
      const message = extractApiError(err).message;
      setError(message);
      announce(message, true); // assertive: a failed registration needs immediate notice
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <View
      style={[styles.container, { backgroundColor: theme.colors.background }]}
      nativeID="main-content"
      role="main"
    >
      <View style={styles.card}>
        <Text style={[styles.title, { color: theme.colors.text }]} accessibilityRole="header">
          Create account
        </Text>
        <TextInput
          style={[styles.input, { borderColor: theme.colors.border, color: theme.colors.text }]}
          placeholder="Username"
          placeholderTextColor={theme.colors.textMuted}
          accessibilityLabel="Username"
          textContentType="username"
          autoComplete="username"
          autoCapitalize="none"
          autoCorrect={false}
          value={username}
          onChangeText={setUsername}
        />
        <TextInput
          style={[styles.input, { borderColor: theme.colors.border, color: theme.colors.text }]}
          placeholder="Password"
          placeholderTextColor={theme.colors.textMuted}
          accessibilityLabel="Password"
          textContentType="newPassword"
          autoComplete="new-password"
          secureTextEntry
          value={password}
          onChangeText={setPassword}
        />
        {error && (
          <Text
            accessibilityRole="alert"
            accessibilityLiveRegion="assertive"
            style={[styles.error, { color: theme.colors.error }]}
          >
            {error}
          </Text>
        )}
        <IconButton
          label="Create account"
          onPress={onSubmit}
          disabled={submitting || !username || !password}
          busy={submitting}
          style={[
            styles.button,
            { backgroundColor: theme.colors.accent, opacity: submitting || !username || !password ? 0.6 : 1 },
          ]}
        >
          {submitting ? <ActivityIndicator color="#fff" /> : <Text style={styles.buttonText}>Create account</Text>}
        </IconButton>
        <View style={styles.linkRow}>
          <Text style={{ color: theme.colors.textMuted }}>Already have an account? </Text>
          <Link href="/login" accessibilityRole="link" style={{ color: theme.colors.accent }}>
            Sign in
          </Link>
        </View>
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: 24 },
  card: { width: '100%', maxWidth: 380, gap: 12 },
  title: { fontSize: 28, fontWeight: '700', marginBottom: 8 },
  input: { borderWidth: 1, borderRadius: 6, paddingHorizontal: 12, paddingVertical: 10, fontSize: 16 },
  button: { paddingVertical: 12, borderRadius: 6, alignItems: 'center', marginTop: 4 },
  buttonText: { color: '#fff', fontWeight: '600', fontSize: 16 },
  error: { fontSize: 14 },
  linkRow: { flexDirection: 'row', justifyContent: 'center', marginTop: 8 },
});
