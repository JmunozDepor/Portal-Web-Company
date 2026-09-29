import { describe, it, expect, vi, afterEach, beforeEach } from 'vitest';
import { renderHook, act, waitFor } from '@testing-library/react';
import { useAuth } from '../../../src/features/auth/useAuth';
import { db } from '../../../src/db/schema';
import { upsertProductos } from '../../../src/db/repositories';

afterEach(() => {
  vi.unstubAllGlobals();
});

beforeEach(async () => {
  await db.authConfig.clear();
  await db.productos.clear();
});

describe('useAuth', () => {
  it('login exitoso guarda el token en authConfig', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ token: 'tok-1', expiresAt: '2026-01-01T00:00:00Z', displayName: 'Juan' }), { status: 200 }),
    ));
    const { result } = renderHook(() => useAuth());
    await waitFor(() => expect(result.current.loading).toBe(false));

    let ok = false;
    await act(async () => {
      ok = await result.current.login('DEPOR', 'jperez', 'secreto');
    });

    expect(ok).toBe(true);
    expect(result.current.token).toBe('tok-1');
    const stored = await db.authConfig.get('singleton');
    expect(stored?.displayName).toBe('Juan');
  });

  it('login fallido (401) no guarda nada y expone error', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('no autorizado', { status: 401 })));
    const { result } = renderHook(() => useAuth());
    await waitFor(() => expect(result.current.loading).toBe(false));

    let ok = true;
    await act(async () => {
      ok = await result.current.login('DEPOR', 'jperez', 'mala');
    });

    expect(ok).toBe(false);
    expect(result.current.token).toBeNull();
    expect(result.current.error).not.toBeNull();
    const stored = await db.authConfig.get('singleton');
    expect(stored).toBeUndefined();
  });

  it('logout limpia authConfig pero no borra el maestro cacheado', async () => {
    await upsertProductos([{ id: 1, barcode: '123', productCode: 'P1' }]);
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ token: 'tok-1', expiresAt: '2026-01-01T00:00:00Z', displayName: 'Juan' }), { status: 200 }),
    ));
    const { result } = renderHook(() => useAuth());
    await waitFor(() => expect(result.current.loading).toBe(false));
    await act(async () => {
      await result.current.login('DEPOR', 'jperez', 'secreto');
    });

    await act(async () => {
      await result.current.logout();
    });

    expect(result.current.token).toBeNull();
    const producto = await db.productos.get(1);
    expect(producto).toBeDefined();
  });
});
