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
  const [challenge, setChallenge] = useState<string | null>(null); const [code, setCode] = useState(''); const [recovery, setRecovery] = useState(false); const [show, setShow] = useState(false);
  const login = useMutation({ mutationFn: () => api.login(u, p), onSuccess: (r) => { setP(''); if (r.mfaRequired && r.mfaChallenge) setChallenge(r.mfaChallenge); else qc.invalidateQueries({ queryKey: ['me'] }); } });
  const second = useMutation({ mutationFn: () => api.loginMfa(challenge!, code), onSuccess: () => { setCode(''); setChallenge(null); qc.invalidateQueries({ queryKey: ['me'] }); }, onError: (e) => { setCode(''); if (String((e as Error).message).includes('sign-in expired')) setChallenge(null); } });
  const submit = (e: FormEvent) => { e.preventDefault(); if (challenge) second.mutate(); else login.mutate(); };
  if (challenge) return (
    <AuthShell>
      <form onSubmit={submit} className="authform">
        <h1>Two-factor authentication</h1>
        <p className="authsub">{recovery ? 'Enter one of your recovery codes.' : 'Enter the 6-digit code from your authenticator app.'}</p>
        <Field label={recovery ? 'Recovery code' : '6-digit code'}><input autoFocus autoComplete="one-time-code" inputMode={recovery ? 'text' : 'numeric'} value={code} onChange={(e) => setCode(e.target.value)} required maxLength={20} /></Field>
        <ErrorBox error={second.error} />
        <button className="btn primary authbtn" disabled={second.isPending || !code}>{second.isPending ? 'Checking…' : 'Verify'}</button>
        <div className="authlinks">
          <button type="button" className="link" onClick={() => { setRecovery(!recovery); setCode(''); }}>{recovery ? 'Use my authenticator app instead' : 'Use a recovery code instead'}</button>
          <button type="button" className="link" onClick={() => { setChallenge(null); setCode(''); }}>Back to sign in</button>
        </div>
      </form>
    </AuthShell>
  );
  return (
    <AuthShell>
      <form onSubmit={submit} className="authform">
        <h1>Welcome back</h1>
        <p className="authsub">Sign in to find devices, ports and switches on your network.</p>
        <Field label="Username"><input autoFocus autoComplete="username" value={u} onChange={(e) => setU(e.target.value)} required /></Field>
        <Field label="Password">
          <span className="pwwrap"><input type={show ? 'text' : 'password'} autoComplete="current-password" value={p} onChange={(e) => setP(e.target.value)} required />
            <button type="button" className="pwtoggle" onClick={() => setShow(!show)} aria-pressed={show}>{show ? 'Hide' : 'Show'}</button></span>
        </Field>
        <ErrorBox error={login.error} />
        <button className="btn primary authbtn" disabled={login.isPending}>{login.isPending ? 'Signing in…' : 'Sign in'}</button>
      </form>
    </AuthShell>
  );
}

const Feature = ({ d, title, text }: { d: string; title: string; text: string }) => (
  <li><span className="authicon"><svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d={d} /></svg></span><span><strong>{title}</strong><small>{text}</small></span></li>
);

/** Two-column sign-in layout: brand and benefits on the left, the form card on the right. */
function AuthShell({ children }: { children: ReactNode }) {
  return (
    <main className="authpage">
      <aside className="authside">
        <div className="authbrand">
          <span className="authlogo" aria-hidden="true"><svg width="26" height="26" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round"><path d="M12 3v6M5 21v-6h14v6M12 9v6M3 21h4M17 21h4M10 21h4" /></svg></span>
          <span><span className="authname">SwitchPort Finder</span><span className="authtag">Port Finder &amp; SNMP Suite</span></span>
        </div>
        <h2>Know exactly where every device is connected.</h2>
        <ul className="authfeatures">
          <Feature d="M21 21l-4.3-4.3M10.5 18a7.5 7.5 0 1 1 0-15 7.5 7.5 0 0 1 0 15z" title="Find any device fast" text="Search by name, IP or MAC and see the switch, port and VLAN." />
          <Feature d="M3 12h4l3-8 4 16 3-8h4" title="Live network health" text="Uptime, utilization, errors and alerts across every switch." />
          <Feature d="M12 3l8 3v6c0 5-3.5 8-8 9-4.5-1-8-4-8-9V6z" title="Read-only and secure" text="Uses SNMP read access only. Nothing on your network is changed." />
        </ul>
        <p className="authfoot">Internal network tool · runs entirely on your own server</p>
      </aside>
      <section className="authcard-wrap">
        <div className="authcard">
          <div className="authcompany"><CompanyMark height={48} /></div>
          {children}
        </div>
        <p className="authsmall">Authorised users only. Sign-ins are logged.</p>
      </section>
    </main>
  );
}
