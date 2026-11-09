import type { ReactNode } from 'react';
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
