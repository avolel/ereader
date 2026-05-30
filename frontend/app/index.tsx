import { Redirect } from 'expo-router';
import { ActivityIndicator, View } from 'react-native';

import { useAuth } from '../src/providers/AuthProvider';

// Root route just routes the user to the right place based on auth state.
export default function Index() {
  const { status } = useAuth();
  if (status === 'loading') {
    return (
      <View style={{ flex: 1, alignItems: 'center', justifyContent: 'center' }}>
        <ActivityIndicator />
      </View>
    );
  }
  return status === 'authed' ? <Redirect href="/library" /> : <Redirect href="/login" />;
}