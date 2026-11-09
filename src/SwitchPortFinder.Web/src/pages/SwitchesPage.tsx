import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { api, MANUFACTURERS } from '../api';
import { ErrorBox, StatusBadge, fmtDate } from '../components/Bits';
import { useAuth } from '../components/Auth';

export default function SwitchesPage() {
  const qc = useQueryClient();
  const { canWrite } = useAuth();
  const { data, isLoading, error } = useQuery({ queryKey: ['switches'], queryFn: api.listSwitches });
  const [vendor, setVendor] = useState('');
  const [filter, setFilter] = useState('');
  const del = useMutation({ mutationFn: api.deleteSwitch, onSuccess: () => qc.invalidateQueries({ queryKey: ['switches'] }) });
  const test = useMutation({ mutationFn: api.testSaved, onSuccess: () => qc.invalidateQueries({ queryKey: ['switches'] }) });
  const scan = useMutation({ mutationFn: api.scan, onSuccess: () => { setNote('Scan queued — follow it on the Discovery page.'); setTimeout(() => qc.invalidateQueries({ queryKey: ['switches'] }), 3000); } });
  const scanAll = useMutation({ mutationFn: api.scanAll, onSuccess: (r) => setNote(`${r.jobIds.length} scan(s) queued — follow them on the Discovery page.`) });
  const [note, setNote] = useState('');

  const rows = (data ?? []).filter((s) =>
    (!vendor || s.manufacturer === vendor) &&
    (!filter || `${s.name} ${s.managementIp}`.toLowerCase().includes(filter.toLowerCase())));

  return (
    <section>
      <div className="toolbar">
        <h1>Switches</h1>
        <div className="spacer" />
        <input placeholder="Filter name / IP" value={filter} onChange={(e) => setFilter(e.target.value)} />
        <select value={vendor} onChange={(e) => setVendor(e.target.value)} aria-label="Manufacturer filter">
          <option value="">All manufacturers</option>
          {MANUFACTURERS.map((m) => <option key={m.value} value={m.value}>{m.label}</option>)}
        </select>
        {canWrite && <button className="btn" disabled={scanAll.isPending || (data?.length ?? 0) === 0} onClick={() => scanAll.mutate()}>Scan all switches</button>}
        {canWrite && <Link className="btn primary" to="/switches/new">+ Add Switch</Link>}
      </div>
      <ErrorBox error={error ?? del.error ?? test.error ?? scan.error ?? scanAll.error} />
      {note && <div className="alert success">{note} <Link to="/discovery">Open Discovery</Link></div>}
      {isLoading ? <p>Loading…</p> : (
        <div className="tablewrap">
          <table>
            <thead><tr><th>Name</th><th>Management IP</th><th>Manufacturer</th><th>Model</th><th>Ports</th><th>Status</th><th>Last Scan</th><th>Actions</th></tr></thead>
            <tbody>
              {rows.map((s) => (
                <tr key={s.id}>
                  <td><Link to={`/switches/${s.id}`}>{s.name}</Link>{s.useAsArpSource && <span className="tag" title="ARP source">ARP</span>}</td>
                  <td>{s.managementIp}</td>
                  <td>{s.manufacturerName}{s.vendorMismatch && <span title="Selected manufacturer differs from detected vendor"> ⚠</span>}</td>
                  <td>{s.model ?? '—'}</td>
                  <td>{s.portCount || '—'}</td>
                  <td><StatusBadge status={s.status} /></td>
                  <td>{fmtDate(s.lastScanAt)}</td>
                  <td className="actions">
                    <Link to={`/switches/${s.id}`}>Open</Link>
                    {canWrite && <>
                      <button className="link" disabled={scan.isPending} onClick={() => scan.mutate(s.id)}>Scan</button>
                      <button className="link" disabled={test.isPending} onClick={() => test.mutate(s.id)} title="Test SNMP connectivity only">Test</button>
                      <Link to={`/switches/${s.id}/edit`}>Edit</Link>
                      <button className="link danger" onClick={() => confirm(`Delete ${s.name}?`) && del.mutate(s.id)}>Delete</button>
                    </>}
                  </td>
                </tr>
              ))}
              {rows.length === 0 && <tr><td colSpan={8} className="muted">No switches.</td></tr>}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
