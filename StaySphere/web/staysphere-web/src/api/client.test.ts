import { api, ApiError, toQuery } from './client';
import { useAuthStore } from '@/lib/auth-store';

const json = (status: number, body: unknown) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

describe('api client', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    useAuthStore.getState().clear();
  });

  it('builds query strings, skipping empty values and repeating arrays', () => {
    expect(toQuery({ a: 1, b: '', c: undefined, d: ['x', 'y'] })).toBe('?a=1&d=x&d=y');
    expect(toQuery({})).toBe('');
  });

  it('turns ProblemDetails into a typed ApiError with field errors', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(json(400, { title: 'One or more fields are invalid.', errors: { email: ['Required'] }, traceId: 't1' }));
    const error = (await api('/x').catch((e: unknown) => e)) as ApiError;
    expect(error).toBeInstanceOf(ApiError);
    expect(error.status).toBe(400);
    expect(error.errors!.email[0]).toBe('Required');
    expect(error.traceId).toBe('t1');
  });

  it('sends the bearer token and idempotency key', async () => {
    useAuthStore.setState({ accessToken: 'abc' });
    const spy = vi.spyOn(globalThis, 'fetch').mockResolvedValue(json(200, { ok: true }));
    await api('/reservations', { method: 'POST', body: {}, idempotencyKey: 'k1' });
    const headers = spy.mock.calls[0][1]!.headers as Record<string, string>;
    expect(headers.Authorization).toBe('Bearer abc');
    expect(headers['Idempotency-Key']).toBe('k1');
  });

  it('refreshes once on 401 and retries the original request', async () => {
    useAuthStore.setState({ accessToken: 'expired' });
    const spy = vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(json(401, { title: 'expired' }))
      .mockResolvedValueOnce(json(200, { accessToken: 'fresh', accessTokenExpiresAt: '', user: { id: '1', roles: [] } }))
      .mockResolvedValueOnce(json(200, { value: 42 }));
    const result = await api<{ value: number }>('/me');
    expect(result.value).toBe(42);
    expect(spy.mock.calls[1][0]).toBe('/api/v1/auth/refresh');
    expect(useAuthStore.getState().accessToken).toBe('fresh');
  });

  it('reports network failures with a friendly message', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValue(new TypeError('Failed to fetch'));
    await expect(api('/x')).rejects.toMatchObject({ status: 0, message: expect.stringContaining('Network error') });
  });
});
