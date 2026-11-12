import { createContext, FormEvent, useContext, useEffect, useState, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, type Me } from '../api';
import { ErrorBox, Field } from './Bits';
import MfaSetup from './MfaSetup';
import { CompanyMark } from './Brand';

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
  // Policy: an administrator without an authenticator app must enrol before anything else.
  if (me.mfaSetupRequired) return <div className="login"><div className="form" style={{ background: 'var(--panel)', border: '1px solid var(--line)', borderRadius: 12, padding: 28, width: 'min(460px,100%)' }}><h1>Set up two-factor authentication</h1><MfaSetup forced onFinished={() => undefined} /><button className="link" onClick={() => out.mutate()}>Sign out</button></div></div>;
  // With authentication off (V1 default) everyone is an administrator.
  return <Ctx.Provider value={{ me, canWrite: !me.authRequired || me.role === 'Admin', logout: () => out.mutate() }}>{children}</Ctx.Provider>;
}

function LoginPage() {
  const qc = useQueryClient();
  const [u, setU] = useState(''); const [p, setP] = useState('');
  const [challenge, setChallenge] = useState<string | null>(null); const [code, setCode] = useState(''); const [recovery, setRecovery] = useState(false);
  const login = useMutation({ mutationFn: () => api.login(u, p), onSuccess: (r) => { setP(''); if (r.mfaRequired && r.mfaChallenge) setChallenge(r.mfaChallenge); else qc.invalidateQueries({ queryKey: ['me'] }); } });
  const second = useMutation({ mutationFn: () => api.loginMfa(challenge!, code), onSuccess: () => { setCode(''); setChallenge(null); qc.invalidateQueries({ queryKey: ['me'] }); }, onError: (e) => { setCode(''); if (String((e as Error).message).includes('sign-in expired')) setChallenge(null); } });
  const submit = (e: FormEvent) => { e.preventDefault(); if (challenge) second.mutate(); else login.mutate(); };
  if (challenge) return (
    <div className="login">
      <form className="form" onSubmit={submit}>
        <h1>Two-factor authentication</h1>
        <p className="muted">{recovery ? 'Enter one of your recovery codes.' : 'Enter the 6-digit code from your authenticator app.'}</p>
        <Field label={recovery ? 'Recovery code' : '6-digit code'}><input autoFocus autoComplete="one-time-code" inputMode={recovery ? 'text' : 'numeric'} value={code} onChange={(e) => setCode(e.target.value)} required maxLength={20} /></Field>
        <ErrorBox error={second.error} />
        <button className="btn primary" disabled={second.isPending || !code}>{second.isPending ? 'Checking…' : 'Verify'}</button>
        <button type="button" className="link" onClick={() => { setRecovery(!recovery); setCode(''); }}>{recovery ? 'Use my authenticator app instead' : 'Use a recovery code instead'}</button>
        <button type="button" className="link" onClick={() => { setChallenge(null); setCode(''); }}>Back to sign in</button>
      </form>
    </div>
  );
  return (
    <div className="login">
      <form className="form" onSubmit={submit}>
        <div style={{ textAlign: 'center', marginBottom: 8 }}><CompanyMark height={56} /></div>
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
