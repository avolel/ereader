import { ScrollView, StyleSheet, Text, View } from 'react-native';

import AccessibleModal from './a11y/AccessibleModal';
import IconButton from './a11y/IconButton';
import { useThemeContext } from '../providers/ThemeProvider';
import { ReadingSettingUpdate, ThemeMode } from '../types';

const THEME_OPTIONS: { label: string; value: ThemeMode }[] = [
  { label: 'Light', value: 'light' },
  { label: 'Dark', value: 'dark' },
  { label: 'System', value: 'system' },
];

const FONT_OPTIONS = [
  { label: 'Serif', value: 'serif' },
  { label: 'Sans', value: 'sans-serif' },
  { label: 'Mono', value: 'monospace' },
];

const FONT_SIZES = [12, 14, 16, 18, 20, 22, 24, 26, 28];
const LINE_SPACINGS = [
  { label: '1.2', value: 1.2 },
  { label: '1.5', value: 1.5 },
  { label: '1.8', value: 1.8 },
  { label: '2.0', value: 2.0 },
];
const MARGIN_PRESETS = [
  { label: 'Compact', h: 24, v: 16 },
  { label: 'Medium', h: 40, v: 20 },
  { label: 'Wide', h: 80, v: 28 },
];

type Props = {
  visible: boolean;
  onClose: () => void;
};

// Edits global settings via ThemeProvider. Per-book override is intentionally
// out of scope for v1 — most users will tune typography once and want it to
// stick across the whole library.
export default function SettingsDrawer({ visible, onClose }: Props) {
  const { theme, globalSetting, updateGlobal } = useThemeContext();
  const { colors } = theme;

  function set(update: ReadingSettingUpdate) {
    // Fire and forget — optimistic update in the hook makes the UI feel snappy.
    void updateGlobal(update);
  }

  return (
    <AccessibleModal
      visible={visible}
      onClose={onClose}
      label="Display settings"
      animationType="slide"
      align="bottom"
      panelStyle={[styles.panel, { backgroundColor: colors.surface, borderColor: colors.border }]}
    >
      <View style={[styles.header, { borderBottomColor: colors.border }]}>
        <Text style={[styles.title, { color: colors.text }]} accessibilityRole="header">
          Display
        </Text>
        <IconButton label="Close settings" onPress={onClose}>
          <Text style={{ color: colors.accent, fontSize: 16 }}>Done</Text>
        </IconButton>
      </View>

      <ScrollView contentContainerStyle={styles.body}>
        <Section title="Theme" colors={colors}>
          <ChipRow
            groupLabel="Theme"
            options={THEME_OPTIONS}
            value={globalSetting.theme}
            onSelect={(v) => set({ theme: v as ThemeMode })}
            colors={colors}
          />
        </Section>

        <Section title="Font" colors={colors}>
          <ChipRow
            groupLabel="Font"
            options={FONT_OPTIONS}
            value={globalSetting.fontFamily}
            onSelect={(v) => set({ fontFamily: v })}
            colors={colors}
          />
        </Section>

        <Section title={`Size — ${globalSetting.fontSize}px`} colors={colors}>
          <ChipRow
            groupLabel="Font size"
            options={FONT_SIZES.map((n) => ({ label: String(n), value: n }))}
            value={globalSetting.fontSize}
            onSelect={(v) => set({ fontSize: v as number })}
            colors={colors}
          />
        </Section>

        <Section title={`Line spacing — ${globalSetting.lineSpacing.toFixed(1)}`} colors={colors}>
          <ChipRow
            groupLabel="Line spacing"
            options={LINE_SPACINGS}
            value={Number(globalSetting.lineSpacing.toFixed(1))}
            onSelect={(v) => set({ lineSpacing: v as number })}
            colors={colors}
          />
        </Section>

        <Section title="Margins" colors={colors}>
          <ChipRow
            groupLabel="Margins"
            options={MARGIN_PRESETS.map((p) => ({ label: p.label, value: p.label }))}
            value={
              MARGIN_PRESETS.find(
                (p) =>
                  p.h === globalSetting.marginHorizontal &&
                  p.v === globalSetting.marginVertical,
              )?.label ?? 'Medium'
            }
            onSelect={(label) => {
              const preset = MARGIN_PRESETS.find((p) => p.label === label);
              if (preset) set({ marginHorizontal: preset.h, marginVertical: preset.v });
            }}
            colors={colors}
          />
        </Section>
      </ScrollView>
    </AccessibleModal>
  );
}

type ChipOption<T extends string | number> = { label: string; value: T };

type ChipRowProps<T extends string | number> = {
  // Accessible name for the radiogroup (the setting being chosen).
  groupLabel: string;
  options: ChipOption<T>[];
  value: T;
  onSelect: (value: T) => void;
  colors: ReturnType<typeof useThemeContext>['theme']['colors'];
};

function ChipRow<T extends string | number>({
  groupLabel,
  options,
  value,
  onSelect,
  colors,
}: ChipRowProps<T>) {
  return (
    <View style={styles.chipRow} accessibilityRole="radiogroup" accessibilityLabel={groupLabel}>
      {options.map((opt) => {
        const active = opt.value === value;
        return (
          <IconButton
            key={String(opt.value)}
            // Active chip was colour-only before; selected state now reaches AT.
            label={opt.label}
            accessibilityRole="radio"
            selected={active}
            onPress={() => onSelect(opt.value)}
            style={[
              styles.chip,
              {
                borderColor: active ? colors.accent : colors.border,
                backgroundColor: active ? colors.accent : 'transparent',
              },
            ]}
          >
            <Text style={{ color: active ? '#fff' : colors.text, fontSize: 13 }}>{opt.label}</Text>
          </IconButton>
        );
      })}
    </View>
  );
}

type SectionProps = {
  title: string;
  colors: ReturnType<typeof useThemeContext>['theme']['colors'];
  children: React.ReactNode;
};

function Section({ title, colors, children }: SectionProps) {
  return (
    <View style={styles.section}>
      <Text style={[styles.sectionTitle, { color: colors.textMuted }]}>{title}</Text>
      {children}
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
  title: { fontSize: 18, fontWeight: '700' },
  body: { padding: 16, gap: 18 },
  section: { gap: 8 },
  sectionTitle: { fontSize: 12, textTransform: 'uppercase', letterSpacing: 0.5 },
  chipRow: { flexDirection: 'row', flexWrap: 'wrap', gap: 8 },
  chip: {
    paddingHorizontal: 12,
    paddingVertical: 6,
    borderRadius: 14,
    borderWidth: 1,
  },
});
