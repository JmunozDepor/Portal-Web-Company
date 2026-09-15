import { describe, it, expect, vi, afterEach } from 'vitest';
import { login, getProductos, upsertSesion, uploadCapturasBatch } from '../../src/api/endpoints';

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('endpoints', () => {
  it('login manda POST con companyCode/username/password en camelCase', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ token: 't', expiresAt: 'x', displayName: 'D' }), { status: 200 }),
    );
    vi.stubGlobal('fetch', fetchMock);
    const result = await login('DEPOR', 'jperez', 'secreto');
    expect(result.token).toBe('t');
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toContain('/api/auditoria-inventario/v1/auth/login');
    expect(init.method).toBe('POST');
    expect(JSON.parse(init.body as string)).toEqual({ companyCode: 'DEPOR', username: 'jperez', password: 'secreto' });
  });

  it('getProductos manda afterId en la query', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ items: [], hasMore: false }), { status: 200 }),
    );
    vi.stubGlobal('fetch', fetchMock);
    await getProductos('tok', 42);
    const [url] = fetchMock.mock.calls[0];
    expect(url).toContain('afterId=42');
  });

  it('upsertSesion manda el body con Bearer token', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response('', { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);
    await upsertSesion('tok', {
      id: 's1', branchId: 1, inventoryNumber: 'INV-1', startedAt: '2026-01-01T00:00:00Z',
      status: 'ACTIVE', validateAgainstMaster: true,
    });
    const [, init] = fetchMock.mock.calls[0];
    expect((init.headers as Record<string, string>).Authorization).toBe('Bearer tok');
  });

  it('uploadCapturasBatch devuelve processed', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ processed: 3 }), { status: 200 }),
    ));
    const result = await uploadCapturasBatch('tok', []);
    expect(result.processed).toBe(3);
  });
});
