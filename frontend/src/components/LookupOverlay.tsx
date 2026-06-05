import { useEffect } from 'react';
import {
  ActivityIndicator,
  Linking,
  ScrollView,
  StyleSheet,
  Text,
  View,
} from 'react-native';

import AccessibleModal from './a11y/AccessibleModal';
import IconButton from './a11y/IconButton';
import { useAnnouncer } from './a11y/useAnnouncer';
import { useDictionary, useWikipedia } from '../hooks/useLookup';
import { useTheme } from '../providers/ThemeProvider';
import type { DictionarySense } from '../types';

type Props = { term: string; onClose: () => void };

// Reference-lookup overlay (FR-25/27). Rendered as a transparent Modal so the
// reader's scroll position is never touched — dismissing returns to the exact
// spot (FR-28). Two stacked sections: offline dictionary on top, online
// Wikipedia summary below, each driven by its own cached query.
export default function LookupOverlay({ term, onClose }: Props) {
  const { colors } = useTheme();
  const { announce } = useAnnouncer();
  const dictionary = useDictionary(term);
  const wikipedia = useWikipedia(term);

  // Announce the dictionary outcome so screen-reader users hear the result of a
  // lookup they can't see resolve (FR-26), including the "no definition" state.
  useEffect(() => {
    if (dictionary.isLoading) return;
    if (dictionary.isError) {
      announce('Could not load the dictionary', true);
      return;
    }
    if (dictionary.data) {
      announce(
        dictionary.data.found
          ? `Definition found for ${term}`
          : `No definition found for ${term}`,
      );
    }
  }, [dictionary.isLoading, dictionary.isError, dictionary.data, term, announce]);

  return (
    <AccessibleModal
      visible
      onClose={onClose}
      label={`Lookup: ${term}`}
      animationType="slide"
      align="bottom"
      panelStyle={[styles.panel, { backgroundColor: colors.surface, borderColor: colors.border }]}
    >
      <View style={[styles.header, { borderBottomColor: colors.border }]}>
        <Text style={[styles.term, { color: colors.text }]} numberOfLines={1} accessibilityRole="header">
          {term}
        </Text>
        <IconButton label="Close lookup" onPress={onClose}>
          <Text style={{ color: colors.accent, fontSize: 16, fontWeight: '600' }}>Done</Text>
        </IconButton>
      </View>

      <ScrollView style={styles.scroll} contentContainerStyle={styles.scrollContent}>
        {/* Dictionary section (FR-25/26) */}
        <Text style={[styles.sectionLabel, { color: colors.textMuted }]} accessibilityRole="header">
          Dictionary
        </Text>
        {dictionary.isLoading ? (
          <ActivityIndicator color={colors.accent} style={styles.loader} />
        ) : dictionary.isError ? (
          <Text style={[styles.body, { color: colors.textMuted }]}>
            Couldn’t load the dictionary.
          </Text>
        ) : dictionary.data && dictionary.data.found ? (
          <DictionarySenses senses={dictionary.data.senses} colors={colors} />
        ) : (
          <Text style={[styles.body, { color: colors.textMuted }]}>
            No definition found for “{dictionary.data?.word ?? term}”.
          </Text>
        )}

        <View style={[styles.divider, { backgroundColor: colors.border }]} />

        {/* Wikipedia section (FR-27) */}
        <Text style={[styles.sectionLabel, { color: colors.textMuted }]} accessibilityRole="header">
          Wikipedia
        </Text>
        {wikipedia.isLoading ? (
          <ActivityIndicator color={colors.accent} style={styles.loader} />
        ) : wikipedia.isError ? (
          <Text style={[styles.body, { color: colors.textMuted }]}>
            Couldn’t reach Wikipedia.
          </Text>
        ) : wikipedia.data && wikipedia.data.found ? (
          <View>
            {wikipedia.data.title ? (
              <Text style={[styles.wikiTitle, { color: colors.text }]}>
                {wikipedia.data.title}
              </Text>
            ) : null}
            {wikipedia.data.extract ? (
              <Text style={[styles.body, { color: colors.text }]}>
                {wikipedia.data.extract}
              </Text>
            ) : null}
            {wikipedia.data.pageUrl ? (
              <IconButton
                label="Read on Wikipedia"
                accessibilityRole="link"
                onPress={() => Linking.openURL(wikipedia.data!.pageUrl!)}
                style={styles.link}
              >
                <Text style={{ color: colors.accent, fontSize: 14 }}>Read on Wikipedia</Text>
              </IconButton>
            ) : null}
          </View>
        ) : (
          <Text style={[styles.body, { color: colors.textMuted }]}>
            No Wikipedia article found.
          </Text>
        )}
      </ScrollView>
    </AccessibleModal>
  );
}

// Senses listed with their part of speech. Pulled out to keep the main render
// readable; grouping by POS is left to the data, which already orders senses.
function DictionarySenses({
  senses,
  colors,
}: {
  senses: DictionarySense[];
  colors: ReturnType<typeof useTheme>['colors'];
}) {
  return (
    <View>
      {senses.map((sense, i) => (
        <View key={i} style={styles.sense}>
          <Text style={[styles.pos, { color: colors.textMuted }]}>{sense.partOfSpeech}</Text>
          <Text style={[styles.body, { color: colors.text }]}>{sense.definition}</Text>
          {sense.examples.map((ex, j) => (
            <Text key={j} style={[styles.example, { color: colors.textMuted }]}>
              “{ex}”
            </Text>
          ))}
        </View>
      ))}
    </View>
  );
}

const styles = StyleSheet.create({
  panel: {
    maxHeight: '80%',
    borderTopLeftRadius: 12,
    borderTopRightRadius: 12,
    borderWidth: 1,
  },
  header: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: 16,
    paddingVertical: 14,
    borderBottomWidth: 1,
  },
  term: { fontSize: 18, fontWeight: '700', flexShrink: 1, marginRight: 12 },
  scroll: { paddingHorizontal: 16 },
  scrollContent: { paddingVertical: 16 },
  sectionLabel: {
    fontSize: 12,
    fontWeight: '600',
    textTransform: 'uppercase',
    letterSpacing: 0.5,
    marginBottom: 8,
  },
  loader: { alignSelf: 'flex-start', marginVertical: 4 },
  divider: { height: 1, marginVertical: 16 },
  sense: { marginBottom: 12 },
  pos: { fontSize: 12, fontStyle: 'italic', marginBottom: 2 },
  body: { fontSize: 15, lineHeight: 22 },
  example: { fontSize: 14, fontStyle: 'italic', marginTop: 2 },
  wikiTitle: { fontSize: 16, fontWeight: '600', marginBottom: 6 },
  link: { marginTop: 10, alignSelf: 'flex-start' },
});
