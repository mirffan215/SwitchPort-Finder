import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { api, type JobDetail, type StepResult } from '../api';

const ICON: Record<StepResult['state'], string> = { Pending: '·', Running: '…', Ok: '✓', Warning: '⚠', Failed: '✗', Skipped: '–' };
const done = (d?: JobDetail) => !!d && !['Queued', 'Running'].includes(d.job.status);

/** Polls the job every 1.2 s and renders the per-step checklist. Calls onDone once when the job has finished. */
export default function ScanProgress({ jobId, onDone, onClose }: { jobId: number; onDone?: (d: JobDetail) => void; onClose?: () => void }) {
  const qc = useQueryClient();
  const { data } = useQuery({ queryKey: ['job', jobId], queryFn: () => api.job(jobId), refetchInterval: (q) => (done(q.state.data) ? false : 1200) });
  useEffect(() => { if (done(data)) { onDone?.(data!); qc.invalidateQueries({ queryKey: ['jobs'] }); } }, [data?.job.status]); // eslint-disable-line react-hooks/exhaustive-deps
  if (!data) return <div className="alert">Queued…</div>;
  const j = data.job;
  const finished = done(data);
  return (
    <div className={`alert ${j.status === 'Failed' ? 'error' : ''}`} role="status">
      <strong>{finished ? (j.status === 'Failed' ? `Scan of ${j.switchName} failed` : `Scan of ${j.switchName} finished`) : `Scanning ${j.switchName}...`}</strong>
      <ul className="steps">
        {data.steps.map((s) => (
          <li key={s.name} className={`step-${s.state.toLowerCase()}`}>
            <span className="ico">{ICON[s.state]}</span> {s.name}{s.message ? <small> — {s.message}</small> : null}
          </li>
        ))}
      </ul>
      {finished && onClose && <button className="link" style={{ float: 'right' }} onClick={onClose} aria-label="Dismiss">✕</button>}
      {finished && <div>{j.status === 'Failed' ? j.summary : `Discovery completed. ${j.portsFound} ports, ${j.devicesFound} devices.`}</div>}
    </div>
  );
}
