import axios from 'axios';

import { ApiError } from '../types';

// The backend always returns { error: { code, message, details? } }.
// This helper digs it out of an axios error (or any error) and falls back to
// a generic message so callers can render *something*.
export function extractApiError(err: unknown): { code: string; message: string } {
  if (axios.isAxiosError<ApiError>(err) && err.response?.data?.error) {
    return {
      code: err.response.data.error.code,
      message: err.response.data.error.message,
    };
  }
  if (err instanceof Error) {
    return { code: 'UNKNOWN', message: err.message };
  }
  return { code: 'UNKNOWN', message: 'Something went wrong.' };
}