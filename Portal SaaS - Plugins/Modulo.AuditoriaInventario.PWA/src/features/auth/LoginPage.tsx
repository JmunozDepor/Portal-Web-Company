import { useState } from 'react';
import { useAuth } from './useAuth';

export function LoginPage() {
  const { error, login } = useAuth();
  const [companyCode, setCompanyCode] = useState('');
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    await login(companyCode, username, password);
    setSubmitting(false);
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
