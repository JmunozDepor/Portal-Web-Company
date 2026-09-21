import { useState } from 'react';

export interface LoginPageProps {
  /** Mensaje de error del ultimo intento, provisto por el unico useAuth() de App. */
  error: string | null;
  onLogin: (companyCode: string, username: string, password: string) => Promise<boolean>;
}

/**
 * Formulario de login. NO instancia useAuth(): el estado de autenticacion vive
 * en App (una sola instancia del hook) y llega por props, para que un login
 * exitoso actualice el guard de rutas.
 */
export function LoginPage({ error, onLogin }: LoginPageProps) {
  const [companyCode, setCompanyCode] = useState('');
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    try {
      await onLogin(companyCode, username, password);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <form onSubmit={handleSubmit}>
      <h1>Auditoría de Inventario</h1>
      {error && <p role="alert">{error}</p>}
      <label>
        Empresa
        <input value={companyCode} onChange={(e) => setCompanyCode(e.target.value)} required />
      </label>
      <label>
        Usuario
        <input value={username} onChange={(e) => setUsername(e.target.value)} required />
      </label>
      <label>
        Contraseña
        <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} required />
      </label>
      <button type="submit" disabled={submitting}>Ingresar</button>
    </form>
  );
}
