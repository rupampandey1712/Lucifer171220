import { useAuthStore } from '@/lib/auth-store';
import type { AuthResponse } from './types';

/** RFC 9457 problem returned by the API, surfaced as a typed error. */
export class ApiError extends Error {
  readonly status: number;
  readonly code?: string;
  readonly errors?: Record<string, string[]>;
  readonly traceId?: string;

  constructor(status: number, title: string, body?: { code?: string; errors?: Record<string, string[]>; traceId?: string }) {
    super(title);
    this.status = status;
    this.code = body?.code;
    this.errors = body?.errors;
    this.traceId = body?.traceId;
  }
}

const BASE = '/api/v1';
let refreshing: Promise<boolean> | null = null;

/** Single-flight silent refresh using the HttpOnly cookie; concurrent 401s share one refresh call. */
export function refreshSession(): Promise<boolean> {
  refreshing ??= (async () => {
    try {
      const res = await fetch(`${BASE}/auth/refresh`, {
        method: 'POST',
        credentials: 'include',
        headers: { 'X-Requested-With': 'staysphere' },
      });
      if (!res.ok) {
        useAuthStore.getState().clear();
        return false;
      }
      useAuthStore.getState().setSession((await res.json()) as AuthResponse);
      return true;
    } catch {
      return false;
    } finally {
      setTimeout(() => (refreshing = null), 0);
    }
  })();
  return refreshing;
}

export interface RequestOptions extends Omit<RequestInit, 'body'> {
  body?: unknown;
  idempotencyKey?: string;
  retryOnUnauthorized?: boolean;
  timeoutMs?: number;
}

export async function api<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { body, idempotencyKey, retryOnUnauthorized = true, timeoutMs = 20000, headers, ...init } = options;
  const token = useAuthStore.getState().accessToken;
  const isForm = body instanceof FormData;
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  if (init.signal) init.signal.addEventListener('abort', () => controller.abort());

  let res: Response;
  try {
    res = await fetch(`${BASE}${path}`, {
      ...init,
      signal: controller.signal,
      credentials: 'include',
      headers: {
        Accept: 'application/json',
        ...(isForm || body === undefined ? {} : { 'Content-Type': 'application/json' }),
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        ...(idempotencyKey ? { 'Idempotency-Key': idempotencyKey } : {}),
        ...headers,
      },
      body: body === undefined ? undefined : isForm ? (body as FormData) : JSON.stringify(body),
    });
  } catch (e) {
    if ((e as Error).name === 'AbortError') throw new ApiError(0, 'The request timed out. Please try again.');
    throw new ApiError(0, 'Network error — check your connection and try again.');
  } finally {
    clearTimeout(timer);
  }

  if (res.status === 401 && retryOnUnauthorized && token) {
    if (await refreshSession()) return api<T>(path, { ...options, retryOnUnauthorized: false });
  }

  if (!res.ok) {
    let problem: { title?: string; code?: string; errors?: Record<string, string[]>; traceId?: string } = {};
    try {
      problem = await res.json();
    } catch {
      /* non-JSON error */
    }
    throw new ApiError(res.status, problem.title ?? defaultMessage(res.status), problem);
  }

  if (res.status === 204 || res.headers.get('content-length') === '0') return undefined as T;
  const text = await res.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

export function defaultMessage(status: number): string {
  switch (status) {
    case 401: return 'Please sign in to continue.';
    case 403: return "You don't have permission to do that.";
    case 404: return "We couldn't find what you were looking for.";
    case 409: return 'That conflicts with a recent change. Please refresh and try again.';
    case 422: return 'Please check the details and try again.';
    case 429: return "You're going a bit fast — please wait a moment.";
    default: return status >= 500 ? 'Something went wrong on our side. Please try again.' : 'Request failed.';
  }
}

export const newIdempotencyKey = () => crypto.randomUUID();

export function toQuery(params: Record<string, unknown>): string {
  const q = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    if (v === undefined || v === null || v === '') continue;
    if (Array.isArray(v)) v.forEach((x) => q.append(k, String(x)));
    else q.set(k, String(v));
  }
  const s = q.toString();
  return s ? `?${s}` : '';
}
