import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import {
  getBookSettings,
  getGlobalSettings,
  updatePosition,
  upsertBookSettings,
  upsertGlobalSettings,
} from '../services/readingSettings';
import { ReadingPositionUpdate, ReadingSetting, ReadingSettingUpdate } from '../types';

export function globalSettingsKey() {
  return ['reading-settings', 'global'] as const;
}

export function bookSettingsKey(bookId: string) {
  return ['reading-settings', 'book', bookId] as const;
}

export function useGlobalSettings(enabled = true) {
  return useQuery({
    queryKey: globalSettingsKey(),
    queryFn: getGlobalSettings,
    enabled,
    // Settings are user-driven and small; let React Query treat them as fresh
    // for a minute to avoid re-fetch storms when the SettingsDrawer mounts
    // alongside the reader.
    staleTime: 60_000,
  });
}

export function useBookSettings(bookId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: bookSettingsKey(bookId ?? ''),
    queryFn: () => getBookSettings(bookId!),
    enabled: enabled && !!bookId,
    staleTime: 60_000,
  });
}

// Optimistic update: write the cached value immediately so the WebView
// re-renders without waiting on the round trip. Rollback on error.
export function useUpsertGlobalSettings() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (update: ReadingSettingUpdate) => upsertGlobalSettings(update),
    onMutate: async (update) => {
      await qc.cancelQueries({ queryKey: globalSettingsKey() });
      const previous = qc.getQueryData<ReadingSetting>(globalSettingsKey());
      if (previous) {
        qc.setQueryData<ReadingSetting>(globalSettingsKey(), {
          ...previous,
          ...stripUndefined(update),
        });
      }
      return { previous };
    },
    onError: (_err, _update, ctx) => {
      if (ctx?.previous) qc.setQueryData(globalSettingsKey(), ctx.previous);
    },
    onSettled: (data) => {
      if (data) qc.setQueryData(globalSettingsKey(), data);
    },
  });
}

export function useUpsertBookSettings(bookId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (update: ReadingSettingUpdate) => upsertBookSettings(bookId, update),
    onMutate: async (update) => {
      await qc.cancelQueries({ queryKey: bookSettingsKey(bookId) });
      const previous = qc.getQueryData<ReadingSetting>(bookSettingsKey(bookId));
      if (previous) {
        qc.setQueryData<ReadingSetting>(bookSettingsKey(bookId), {
          ...previous,
          ...stripUndefined(update),
        });
      }
      return { previous };
    },
    onError: (_err, _update, ctx) => {
      if (ctx?.previous) qc.setQueryData(bookSettingsKey(bookId), ctx.previous);
    },
    onSettled: (data) => {
      if (data) qc.setQueryData(bookSettingsKey(bookId), data);
    },
  });
}

// Position writes are high-frequency (debounced from scroll). Cache update
// happens optimistically; rollback isn't worth it for the small risk of
// the cached position drifting one save behind on a network blip.
export function useUpdatePosition(bookId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (update: ReadingPositionUpdate) => updatePosition(bookId, update),
    onSuccess: (data) => {
      qc.setQueryData(bookSettingsKey(bookId), data);
    },
  });
}

function stripUndefined<T extends object>(obj: T): Partial<T> {
  const out: Partial<T> = {};
  for (const [k, v] of Object.entries(obj)) {
    if (v !== undefined) (out as Record<string, unknown>)[k] = v;
  }
  return out;
}