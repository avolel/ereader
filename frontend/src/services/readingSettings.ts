import {
  ReadingPositionUpdate,
  ReadingSetting,
  ReadingSettingUpdate,
} from '../types';
import { api } from './api';

export async function getGlobalSettings(): Promise<ReadingSetting> {
  const { data } = await api.get<ReadingSetting>('/api/v1/reading-settings/me');
  return data;
}

export async function upsertGlobalSettings(
  update: ReadingSettingUpdate,
): Promise<ReadingSetting> {
  const { data } = await api.put<ReadingSetting>('/api/v1/reading-settings/me', update);
  return data;
}

export async function getBookSettings(bookId: string): Promise<ReadingSetting> {
  const { data } = await api.get<ReadingSetting>(
    `/api/v1/reading-settings/books/${bookId}`,
  );
  return data;
}

export async function upsertBookSettings(
  bookId: string,
  update: ReadingSettingUpdate,
): Promise<ReadingSetting> {
  const { data } = await api.put<ReadingSetting>(
    `/api/v1/reading-settings/books/${bookId}`,
    update,
  );
  return data;
}

export async function deleteBookSettings(bookId: string): Promise<void> {
  await api.delete(`/api/v1/reading-settings/books/${bookId}`);
}

export async function updatePosition(
  bookId: string,
  update: ReadingPositionUpdate,
): Promise<ReadingSetting> {
  const { data } = await api.put<ReadingSetting>(
    `/api/v1/reading-settings/books/${bookId}/position`,
    update,
  );
  return data;
}