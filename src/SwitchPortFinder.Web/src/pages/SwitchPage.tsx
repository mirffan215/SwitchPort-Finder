import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useParams } from 'react-router-dom';
import { api } from '../api';
import { ErrorBox, StatusBadge, TestResultBox, fmtDate } from '../components/Bits';

export default function SwitchPage() {
  const id = Number(useParams().id);
  const qc = useQueryClient();
  const { data: s, error, isLoading } = useQuery({ queryKey: ['switch', id], queryFn: () => api.getSwitch(id) });
  const test = useMutation({ mutationFn: () => api.testSaved(id), onSuccess: () => { qc.invalidateQueries({ queryKey: ['switch', id] }); qc.invalidateQueries({ queryKey: ['switches'] }); } });
  if (isLoading) return <p>Loading…</p>;
  if (error || !s) return <ErrorBox error={error ?? new Error('Switch not found')} />;
  return (
    <section>
      <div className="toolbar">
        <h1>{s.name}</h1>
        <span className="muted">· {s.managementIp} · {s.manufacturerName}</span>
        <StatusBadge status={s.status} />
        <div className="spacer" />
        <button className="btn" disabled={test.isPending} onClick={() => test.mutate()}>{test.isPending ? 'Testing…' : 'Test SNMP'}</button>
        <button className="btn" disabled title="Discovery scanning arrives in Phase 2">Scan Now</button>
        <Link className="btn" to={`/switches/${id}/edit`}>Edit</Link>
      </div>
      {test.data && <TestResultBox r={test.data} />}
      <ErrorBox error={test.error} />
      {s.vendorMismatch && <div className="alert warn">⚠ Selected manufacturer ({s.manufacturerName}) differs from the detected vendor.</div>}
      <dl className="kv big">
        <dt>Management IP</dt><dd>{s.managementIp}</dd>
        <dt>Manufacturer</dt><dd>{s.manufacturerName}</dd>
        <dt>Model</dt><dd>{s.model ?? '—'}</dd>
        <dt>Location</dt><dd>{s.location ?? '—'}</dd>
        <dt>ARP source</dt><dd>{s.useAsArpSource ? 'Yes' : 'No (auto-detected during discovery)'}</dd>
        <dt>SNMP</dt><dd>{s.credentialProfileName ? `Profile: ${s.credentialProfileName}` : s.credential ? `${s.credential.version} (credentials stored encrypted)` : '—'}</dd>
        <dt>sysName</dt><dd>{s.sysName ?? '—'}</dd>
        <dt>sysDescr</dt><dd>{s.sysDescr ?? '—'}</dd>
        <dt>sysObjectID</dt><dd>{s.sysObjectId ?? '—'}</dd>
        <dt>Last scan</dt><dd>{fmtDate(s.lastScanAt)}</dd>
      </dl>
      <p className="muted">Ports, MAC addresses and IPs appear here after Phase 2 (discovery).</p>
    </section>
  );
}
