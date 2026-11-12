import { useEffect, useRef, useState, type FormEvent } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import QRCode from 'qrcode';
import { api, type MfaSetup as Setup } from '../api';
import { ErrorBox, Field } from './Bits';

/** Draws the otpauth:// URI as a QR code on a canvas (no external service: the secret never leaves this page). */
function Qr({ value }: { value: string }) {
  const ref = useRef<HTMLCanvasElement>(null);
  useEffect(() => { if (ref.current) QRCode.toCanvas(ref.current, value, { width: 200, margin: 1, errorCorrectionLevel: 'M' }).catch(() => undefined); }, [value]);
  return <canvas ref={ref} className="qr" role="img" aria-label="QR code for your authenticator app" />;
}

export function RecoveryCodes({ codes, onDone }: { codes: string[]; onDone: () => void }) {
  const [saved, setSaved] = useState(false);
  const download = () => {
    const blob = new Blob([`SwitchPort Finder recovery codes\nEach code works once. Keep this file somewhere safe and offline.\n\n${codes.join('\n')}\n`], { type: 'text/plain' });
    const a = document.createElement('a'); a.href = URL.createObjectURL(blob); a.download = 'switchport-finder-recovery-codes.txt'; a.click(); URL.revokeObjectURL(a.href);
  };
  return (
    <div className="form">
      <div className="alert warn"><strong>Save these recovery codes now.</strong> They are shown only once. Each works one time if you lose your phone.</div>
      <ul className="recovery mono">{codes.map((c) => <li key={c}>{c}</li>)}</ul>
      <div className="buttons"><button type="button" className="btn" onClick={download}>Download .txt</button><button type="button" className="btn" onClick={() => navigator.clipboard?.writeText(codes.join('\n')).catch(() => undefined)}>Copy</button></div>
      <label className="check"><input type="checkbox" checked={saved} onChange={(e) => setSaved(e.target.checked)} /> I have saved my recovery codes</label>
      <div className="buttons"><button type="button" className="btn primary" disabled={!saved} onClick={onDone}>Continue</button></div>
    </div>
  );
}

/** Three steps: confirm password, scan the QR code and enter a code, save the recovery codes. */
export default function MfaSetup({ forced, adding, onFinished, onCancel }: { forced?: boolean; adding?: boolean; onFinished: () => void; onCancel?: () => void }) {
  const qc = useQueryClient();
  const [pw, setPw] = useState(''); const [code, setCode] = useState(''); const [name, setName] = useState(''); const [cur, setCur] = useState('');
  const [setup, setSetup] = useState<Setup | null>(null); const [codes, setCodes] = useState<string[] | null>(null);
  const start = useMutation({ mutationFn: () => api.mfaSetup(pw, name.trim(), cur), onSuccess: (s) => { setSetup(s); setPw(''); setCur(''); } });
  const enable = useMutation({ mutationFn: () => api.mfaEnable(code), onSuccess: (r) => { setSetup(null); setCode(''); if (r.recoveryCodes.length > 0) setCodes(r.recoveryCodes); else finish(); } });
  const finish = () => { qc.invalidateQueries({ queryKey: ['me'] }); qc.invalidateQueries({ queryKey: ['mfa-devices'] }); qc.invalidateQueries({ queryKey: ['users'] }); onFinished(); };
  if (codes) return <RecoveryCodes codes={codes} onDone={finish} />;
  if (setup) return (
    <form className="form" onSubmit={(e: FormEvent) => { e.preventDefault(); enable.mutate(); }}>
      <p>1. Open an authenticator app (Microsoft Authenticator, Google Authenticator, Authy, 1Password…) and add an account by scanning this QR code.</p>
      <Qr value={setup.otpauthUri} />
      <p className="small muted">Cannot scan? Enter this key manually (time-based): <code className="mono">{setup.secret.match(/.{1,4}/g)?.join(' ')}</code></p>
      <p>2. Enter the 6-digit code the app shows now.</p>
      <Field label="6-digit code"><input inputMode="numeric" autoComplete="one-time-code" pattern="[0-9 ]*" maxLength={7} autoFocus value={code} onChange={(e) => setCode(e.target.value)} required /></Field>
      <ErrorBox error={enable.error} />
      <div className="buttons"><button className="btn primary" disabled={enable.isPending || code.replace(/\D/g, '').length !== 6}>Verify and turn on</button></div>
    </form>
  );
  return (
    <form className="form" onSubmit={(e: FormEvent) => { e.preventDefault(); start.mutate(); }}>
      {forced && <div className="alert warn">Administrators must use two-factor authentication. Set up an authenticator app to continue.</div>}
      {!adding && <p className="muted">Two-factor authentication asks for a code from your phone each time you sign in, so a stolen password alone is not enough.</p>}
      {adding && <Field label="Name for this phone or app" hint="For example “Waqar's phone”. It helps you tell the apps apart when you remove one later."><input value={name} maxLength={60} onChange={(e) => setName(e.target.value)} placeholder="Authenticator" /></Field>}
      <Field label="Confirm your password"><input type="password" autoComplete="current-password" value={pw} onChange={(e) => setPw(e.target.value)} required autoFocus /></Field>
      {adding && <Field label="Current code from an app you already use" hint="Proves you are allowed to add another phone."><input inputMode="numeric" autoComplete="one-time-code" value={cur} onChange={(e) => setCur(e.target.value)} required /></Field>}
      <ErrorBox error={start.error} />
      <div className="buttons"><button className="btn primary" disabled={start.isPending || !pw || (adding && !cur)}>{adding ? 'Add this phone' : 'Set up authenticator app'}</button>{onCancel && <button type="button" className="btn" onClick={onCancel}>Cancel</button>}</div>
    </form>
  );
}
