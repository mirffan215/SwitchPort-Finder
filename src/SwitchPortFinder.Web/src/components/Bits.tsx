import { useEffect, useRef, useState, type ReactNode } from 'react';
import { ApiError, type TestResult } from '../api';

export function StatusBadge({ status }: { status: string }) {
  const cls = status === 'Reachable' ? 'ok' : status === 'Unreachable' ? 'bad' : 'muted';
  return <span className={`badge ${cls}`}>{status}</span>;
}

export function ErrorBox({ error }: { error: unknown }) {
  if (!error) return null;
  const msg = error instanceof Error ? error.message : String(error);
  const fields = error instanceof ApiError ? Object.entries(error.fieldErrors) : [];
  return (
    <div className="alert error" role="alert">
      {msg}
      {fields.length > 0 && <ul>{fields.map(([k, v]) => <li key={k}>{v.join(' ')}</li>)}</ul>}
    </div>
  );
}

export function TestResultBox({ r }: { r: TestResult }) {
  return (
    <div className={`alert ${r.success ? 'success' : 'error'}`} role="status">
      <strong>{r.success ? '✓ ' : '✗ '}{r.message}</strong>
      {r.success && (
        <dl className="kv">
          <dt>sysName</dt><dd>{r.sysName ?? '—'}</dd>
          <dt>sysDescr</dt><dd>{r.sysDescr ?? '—'}</dd>
          <dt>sysObjectID</dt><dd>{r.sysObjectId ?? '—'}</dd>
          <dt>Detected vendor</dt><dd>{r.detectedManufacturerName ?? '—'}</dd>
          <dt>Response</dt><dd>{r.elapsedMs} ms</dd>
        </dl>
      )}
      {r.vendorMismatch && <div className="warn">⚠ The selected manufacturer differs from the detected vendor.</div>}
    </div>
  );
}

export function Field({ label, hint, children }: { label: string; hint?: string; children: ReactNode }) {
  return <label className="field"><span>{label}</span>{children}{hint && <small>{hint}</small>}</label>;
}

export const fmtDate = (s?: string | null) => (s ? new Date(s).toLocaleString() : '—');

export function ago(s?: string | null): string {
  if (!s) return '—';
  const sec = Math.max(0, (Date.now() - new Date(s).getTime()) / 1000);
  if (sec < 60) return 'just now';
  if (sec < 3600) return `${Math.floor(sec / 60)} minute${Math.floor(sec / 60) === 1 ? '' : 's'} ago`;
  if (sec < 86400) return `${Math.floor(sec / 3600)} hour${Math.floor(sec / 3600) === 1 ? '' : 's'} ago`;
  return `${Math.floor(sec / 86400)} day(s) ago`;
}

const TYPE_LABEL: Record<string, string> = { Endpoint: 'Endpoint', SwitchUplink: 'Switch Uplink', Unknown: 'Unknown', Down: 'Down' };
export function PortTypeBadge({ type }: { type: string }) {
  const cls = type === 'Endpoint' ? 'ok' : type === 'SwitchUplink' ? 'info' : type === 'Down' ? 'muted' : 'warnb';
  const icon = type === 'Endpoint' ? '●' : type === 'SwitchUplink' ? '⇅' : type === 'Down' ? '○' : '?';
  return <span className={`badge ${cls}`}>{icon} {TYPE_LABEL[type] ?? type}</span>;
}

export function CategoryBadge({ c, manual }: { c?: string | null; manual?: boolean }) {
  if (!c) return <span className="muted">—</span>;
  const label = ({ AccessPoint: 'Access point', IpPhone: 'IP phone', Printer: 'Printer', Camera: 'Camera', Server: 'Server', NetworkSwitch: 'Network switch', Other: 'Other' } as Record<string, string>)[c] ?? c;
  return <span className={`badge ${c === 'AccessPoint' ? 'info' : c === 'IpPhone' ? 'ok' : 'muted'}`} title={manual ? 'Set manually' : 'Detected by the category rules'}>{label}{manual ? ' ✎' : ''}</span>;
}

/** A coloured status dot with a label (used for device, port and job status in every table). */
export function Dot({ kind, label, title }: { kind: 'ok' | 'warn' | 'bad' | 'muted'; label: string; title?: string }) {
  return <span className="statusline" title={title}><span className={`stdot ${kind}`} aria-hidden="true" /><span>{label}</span></span>;
}

export function StateDot({ state, lastSeen }: { state: string; lastSeen?: string | null }) {
  const label = state === 'Active' ? 'Online' : state;
  const why = state === 'Active' ? 'Found by the latest scan of its switch' : state === 'Offline' ? 'Not found by the latest scan of its switch' : 'Not found by the scans of its switch for a long time';
  return <div className="statuscell"><Dot kind={state === 'Active' ? 'ok' : state === 'Offline' ? 'warn' : 'muted'} label={label} title={why} />{lastSeen !== undefined && <div className="muted small" title={fmtDate(lastSeen)}>{ago(lastSeen)}</div>}</div>;
}

export function StateBadge({ state }: { state: string }) {
  const label = state === 'Active' ? 'Online' : state;
  const why = state === 'Active' ? 'Found by the latest scan of its switch' : state === 'Offline' ? 'Not found by the latest scan of its switch' : 'Not found by the scans of its switch for a long time';
  return <span className={`badge ${state === 'Active' ? 'ok' : state === 'Offline' ? 'warnb' : 'muted'}`} title={why}>{label}</span>;
}

export function ConfidenceBadge({ c }: { c: string }) {
  const label = c === 'ConfirmedSwitch' ? 'Confirmed' : c === 'LikelySwitch' ? 'Likely' : c === 'Endpoint' ? 'Endpoint' : 'Unknown';
  return <span className={`badge ${c === 'ConfirmedSwitch' ? 'ok' : c === 'LikelySwitch' ? 'warnb' : 'muted'}`} title="How strongly the evidence supports this classification">{label}</span>;
}

/** The "⋯" menu of a row: closes on Escape, on an outside click and after a choice. */
export function RowMenu({ label, children }: { label: string; children: (close: () => void) => ReactNode }) {
  const [open, setOpen] = useState(false); const box = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) return;
    const away = (e: MouseEvent) => { if (!box.current?.contains(e.target as Node)) setOpen(false); };
    const esc = (e: KeyboardEvent) => { if (e.key === 'Escape') setOpen(false); };
    document.addEventListener('mousedown', away); document.addEventListener('keydown', esc);
    return () => { document.removeEventListener('mousedown', away); document.removeEventListener('keydown', esc); };
  }, [open]);
  return (
    <div className="rowmenu" ref={box}>
      <button type="button" className="iconbtn" aria-haspopup="menu" aria-expanded={open} aria-label={label} onClick={() => setOpen(!open)}>⋯</button>
      {open && <div className="rowmenu-list" role="menu">{children(() => setOpen(false))}</div>}
    </div>
  );
}


