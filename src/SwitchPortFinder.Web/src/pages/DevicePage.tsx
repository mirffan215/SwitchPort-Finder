import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate, useParams } from 'react-router-dom';
import DeviceForm from '../components/DeviceForm';
import { useAuth } from '../components/Auth';
import { api } from '../api';
import { CategoryBadge, ErrorBox, StateBadge, ago, fmtDate } from '../components/Bits';

export default function DevicePage() {
  const id = Number(useParams().id);
  const { canWrite } = useAuth(); const nav = useNavigate(); const qc = useQueryClient(); const [editing, setEditing] = useState(false);
  const del = useMutation({ mutationFn: () => api.deleteDevice(id), onSuccess: () => { qc.invalidateQueries({ queryKey: ['devices'] }); nav('/devices'); } });
  const { data, error, isLoading } = useQuery({ queryKey: ['device', id], queryFn: () => api.device(id) });
  if (isLoading) return <p>Loading…</p>;
  if (error || !data) return <ErrorBox error={error ?? new Error('Device not found')} />;
  const d = data.device;
  return (
    <section>
      <div className="toolbar">
        <h1>{d.hostname}</h1><StateBadge state={d.state} /><div className="spacer" />
        {d.switchId ? <Link className="btn primary" to={`/switches/${d.switchId}?port=${d.ifIndex}`}>Open Switch</Link> : null}
        {canWrite && <button className="btn" onClick={() => setEditing(!editing)}>Edit details</button>}
        {canWrite && d.manual && <button className="btn" onClick={() => confirm('Delete this manually added device?') && del.mutate()}>Delete</button>}
        <Link className="btn" to={`/devices/${d.id}/trace`}>Trace Device</Link>
      </div>
      {editing && <DeviceForm device={d} onDone={() => setEditing(false)} onCancel={() => setEditing(false)} />}
      <ErrorBox error={del.error} />
      <dl className="kv big">
        <dt>Hostname</dt><dd>{d.hostname} <span className="muted">({d.hostnameSource === 'None' ? 'not resolved' : d.hostnameSource})</span></dd>
        <dt>IP address</dt><dd>{d.ip ?? 'Unknown'}{data.ipSourceSwitch && <span className="muted"> — from ARP on {data.ipSourceSwitch}, {ago(data.ipObservedAt)}</span>}</dd>
        <dt>MAC address</dt><dd className="mono">{d.mac}</dd>
        <dt>Product / manufacturer</dt><dd>{d.vendor ?? <span className="muted">Unknown (not in the IEEE registry)</span>} <span className="muted small">— from the MAC address; it names the maker of the network adapter, not the model</span></dd>
        {data.external && <><dt>{data.external.source}</dt><dd>{[data.external.user, data.external.os, data.external.detail].filter(Boolean).join(' · ') || '—'}</dd></>}
        {(d.owner || d.notes) && <><dt>Owner</dt><dd>{d.owner ?? '—'}</dd><dt>Notes</dt><dd>{d.notes ?? '—'}</dd></>}
        <dt>Type</dt><dd><CategoryBadge c={d.category} manual={d.categoryManual} /> {d.category && <span className="muted small">— {d.categoryReason || 'rules'}</span>}</dd>
        <dt>Switch</dt><dd>{d.switchId ? <Link to={`/switches/${d.switchId}`}>{d.switchName}</Link> : '—'}</dd>
        <dt>Port</dt><dd>{d.portName ?? '—'}</dd>
        <dt>VLAN</dt><dd>{d.vlan ?? '—'}</dd>
        <dt>Status</dt><dd><StateBadge state={d.state} /></dd>
        <dt>First seen</dt><dd>{fmtDate(d.firstSeen)}</dd>
        <dt>Last seen</dt><dd>{fmtDate(d.lastSeen)} <span className="muted">({ago(d.lastSeen)})</span></dd>
        <dt>Location confidence</dt><dd className={d.lowConfidence ? 'warn' : ''}>{data.locationNote}</dd>
      </dl>
      <h2>Where this MAC was seen</h2>
      <div className="tablewrap"><table>
        <thead><tr><th>Switch</th><th>Port</th><th>Kind</th><th>MACs on port</th><th>VLAN</th><th>Last seen</th></tr></thead>
        <tbody>{data.sightings.map((s) => (
          <tr key={s.portId} className={s.isUplink ? 'dim' : ''}>
            <td><Link to={`/switches/${s.switchId}`}>{s.switchName}</Link></td>
            <td><Link to={`/switches/${s.switchId}?port=${s.ifIndex}`}>{s.portName}</Link></td>
            <td>{s.isUplink ? 'Switch uplink (not a location)' : 'Edge port'}</td><td>{s.macsOnPort}</td><td>{s.vlan ?? '—'}</td><td>{ago(s.lastSeen)}</td>
          </tr>))}
          {data.sightings.length === 0 && <tr><td colSpan={6} className="muted">No sightings.</td></tr>}
        </tbody>
      </table></div>
    </section>
  );
}
