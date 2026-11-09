import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, type JobSummary } from '../api';
import { ErrorBox, fmtDate } from '../components/Bits';
import ScanProgress from '../components/ScanProgress';

const BADGE: Record<string, string> = { Completed: 'ok', CompletedWithWarnings: 'warnb', Failed: 'bad', Running: 'info', Queued: 'muted' };

export default function DiscoveryPage() {
  const { data, error } = useQuery({ queryKey: ['jobs'], queryFn: () => api.jobs(), refetchInterval: (q) => (q.state.data?.some((j) => j.status === 'Queued' || j.status === 'Running') ? 2000 : false) });
  const qc = useQueryClient();
  const [open, setOpen] = useState<JobSummary | null>(null);
  const [status, setStatus] = useState(''); const [sw, setSw] = useState('');
  const scanAll = useMutation({ mutationFn: api.scanAll, onSuccess: () => qc.invalidateQueries({ queryKey: ['jobs'] }) });
  const switches = [...new Set(data?.map((j) => j.switchName) ?? [])].sort();
  const rows = (data ?? []).filter((j) => (!status || j.status === status) && (!sw || j.switchName === sw));
  const detail = useQuery({ queryKey: ['job', open?.id], queryFn: () => api.job(open!.id), enabled: !!open });
  return (
    <section>
      <div className="toolbar"><h1>Discovery</h1><div className="spacer" />
        <select value={sw} onChange={(e) => setSw(e.target.value)} aria-label="Switch filter"><option value="">All switches</option>{switches.map((n) => <option key={n}>{n}</option>)}</select>
        <select value={status} onChange={(e) => setStatus(e.target.value)} aria-label="Status filter"><option value="">All statuses</option><option>Completed</option><option value="CompletedWithWarnings">Completed (warnings)</option><option>Failed</option><option>Running</option><option>Queued</option></select>
        <button className="btn primary" disabled={scanAll.isPending} onClick={() => scanAll.mutate()}>Scan all switches</button></div>
      <ErrorBox error={error ?? scanAll.error} />
      <div className="tablewrap">
        <table>
          <thead><tr><th>Switch</th><th>Started</th><th>Completed</th><th>Duration</th><th>Status</th><th>Devices</th><th>Ports</th><th>Neighbors</th><th>Errors</th></tr></thead>
          <tbody>
            {rows.map((j) => (
              <tr key={j.id} className="clickable" onClick={() => setOpen(j)}>
                <td>{j.switchName}</td><td>{fmtDate(j.startedAt ?? j.queuedAt)}</td><td>{fmtDate(j.completedAt)}</td>
                <td>{j.durationSeconds != null ? `${j.durationSeconds.toFixed(1)} s` : '—'}</td>
                <td><span className={`badge ${BADGE[j.status]}`}>{j.status === 'CompletedWithWarnings' ? 'Completed (warnings)' : j.status}</span></td>
                <td>{j.devicesFound}</td><td>{j.portsFound}</td><td>{j.neighborsFound}</td><td>{j.errorCount}</td>
              </tr>
            ))}
            {rows.length === 0 && <tr><td colSpan={9} className="muted">No discovery jobs{data?.length ? ' match the filters' : ' yet'}.</td></tr>}
          </tbody>
        </table>
      </div>
      {open && (
        <div className="panel" style={{ marginTop: 16 }}>
          <div className="toolbar"><h3>Job #{open.id} · {open.switchName}</h3><div className="spacer" /><button className="link" onClick={() => setOpen(null)}>✕</button></div>
          <ScanProgress jobId={open.id} />
          {detail.data && detail.data.errors.length > 0 && (
            <>
              <h4>Errors and warnings</h4>
              <ul>{detail.data.errors.map((e, i) => <li key={i}><strong>{e.step}</strong> <code>{e.code}</code> — {e.message}</li>)}</ul>
            </>
          )}
        </div>
      )}
    </section>
  );
}
