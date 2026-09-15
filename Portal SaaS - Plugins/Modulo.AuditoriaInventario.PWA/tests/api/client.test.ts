import { describe, it, expect, vi, afterEach } from 'vitest';
import { request, ApiError } from '../../src/api/client';

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('client.request', () => {
  it('parsea JSON en una respuesta 200', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ ok: true }), { status: 200 }),
    ));
    const result = await request<{ ok: boolean }>('/x');
    expect(result).toEqual({ ok: true });
  });

  it('devuelve undefined en una respuesta 200 sin body', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 200 })));
    const result = await request<void>('/x');
    expect(result).toBeUndefined();
  });

  it('lanza ApiError con el status en una respuesta 401', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('no autorizado', { status: 401 })));
    await expect(request('/x')).rejects.toMatchObject(new ApiError(401, 'no autorizado'));
  });

  it('agrega el header Authorization cuando se pasa token', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response('', { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);
    await request('/x', { token: 'abc123' });
    const [, init] = fetchMock.mock.calls[0];
    expect((init.headers as Record<string, string>).Authorization).toBe('Bearer abc123');
  });
});
