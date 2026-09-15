import { useCallback, useEffect, useState } from 'react';
import { login as apiLogin } from '../../api/endpoints';
import { ApiError } from '../../api/client';
import { getAuthConfig, setAuthConfig, clearAuthConfig } from '../../db/repositories';

export function useAuth() {
  const [token, setToken] = useState<string | null>(null);
  const [displayName, setDisplayName] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    getAuthConfig().then((cfg) => {
      setToken(cfg?.token ?? null);
      setDisplayName(cfg?.displayName ?? null);
      setLoading(false);
    });
  }, []);

  const login = useCallback(async (companyCode: string, username: string, password: string): Promise<boolean> => {
    setError(null);
    try {
      const result = await apiLogin(companyCode, username, password);
      await setAuthConfig({
        token: result.token,
        expiresAt: result.expiresAt,
        displayName: result.displayName,
        companyCode,
      });
      setToken(result.token);
      setDisplayName(result.displayName);
      return true;
    } catch (err) {
      setError(err instanceof ApiError ? 'Usuario, empresa o contraseña incorrectos.' : 'No se pudo conectar con el servidor.');
      return false;
    }
  }, []);

  const logout = useCallback(async () => {
    await clearAuthConfig();
    setToken(null);
    setDisplayName(null);
  }, []);

  return { token, displayName, loading, error, login, logout };
}
