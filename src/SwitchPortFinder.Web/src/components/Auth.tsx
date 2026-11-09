import { createContext, FormEvent, useContext, useEffect, useState, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, type Me } from '../api';
import { ErrorBox, Field } from './Bits';

interface AuthState { me: Me; canWrite: boolean; logout: () => void }
const Ctx = createContext<AuthState | null>(null);
export const useAuth = () => useContext(Ctx)!;

/** Fetches the session once; when login is required and nobody is signed in, shows the login page instead of the app. */
export function AuthGate({ children }: { children: ReactNode }) {
  const qc = useQueryClient();
  const { data: me, isLoading, error } = useQuery({ queryKey: ['me'], queryFn: api.me, retry: false });
  // any 401 from the API means the session ended: re-check
  useEffect(() => { const h = () => qc.invalidateQueries({ queryKey: ['me'] }); window.addEventListener('spf-unauthorized', h); return () => window.removeEventListener('spf-unauthorized', h); }, [qc]);
  const out = useMutation({ mutationFn: api.logout, onSuccess: () => { qc.clear(); qc.invalidateQueries({ queryKey: ['me'] }); } });
  if (isLoading) return <p style={{ padding: 20 }}>Loading…</p>;
  if (error || !me) return <div style={{ padding: 20 }}><ErrorBox error={error ?? new Error('Cannot reach the server')} /></div>;
  if (me.authRequired && !me.authenticated) return <LoginPage />;
  // With authentication off (V1 default) everyone is an administrator.
  return <Ctx.Provider value={{ me, canWrite: !me.authRequired || me.role === 'Admin', logout: () => out.mutate() }}>{children}</Ctx.Provider>;
}

function LoginPage() {
  const qc = useQueryClient();
  const [u, setU] = useState(''); const [p, setP] = useState('');
  const login = useMutation({ mutationFn: () => api.login(u, p), onSuccess: () => { setP(''); qc.invalidateQueries({ queryKey: ['me'] }); } });
  const submit = (e: FormEvent) => { e.preventDefault(); login.mutate(); };
  return (
    <div className="login">
      <form className="form" onSubmit={submit}>
        <h1>SwitchPort Finder</h1>
        <p className="muted">Sign in to continue.</p>
        <Field label="Username"><input autoFocus autoComplete="username" value={u} onChange={(e) => setU(e.target.value)} required /></Field>
        <Field label="Password"><input type="password" autoComplete="current-password" value={p} onChange={(e) => setP(e.target.value)} required /></Field>
        <ErrorBox error={login.error} />
        <button className="btn primary" disabled={login.isPending}>{login.isPending ? 'Signing in…' : 'Sign in'}</button>
      </form>
    </div>
  );
}
