import { useQuery } from '@tanstack/react-query';
import { Link, useSearchParams } from 'react-router-dom';
import { api, type DeviceRow } from '../api';
import { ErrorBox, PortTypeBadge, StateBadge, StatusBadge, ago } from '../components/Bits';

export function DeviceCard({ d }: { d: DeviceRow }) {
  return (
    <div className="result">
      <div className="result-head"><span className="tag">DEVICE FOUND</span> <Link to={`/devices/${d.id}`}><strong>{d.hostname}</strong></Link></div>
      <dl className="kv cols">
        <dt>IP</dt><dd>{d.ip ?? <span className="muted">Unknown</span>}</dd>
        <dt>MAC</dt><dd className="mono">{d.mac}</dd>
        <dt>Product</dt><dd>{d.vendor ?? <span className="muted">Unknown</span>}</dd>
        <dt>Switch</dt><dd>{d.switchId ? <Link to={`/switches/${d.switchId}`}>{d.switchName}</Link> : <span className="muted">not determined yet</span>}</dd>
        <dt>Port</dt><dd>{d.portName ?? '—'}</dd>
        <dt>VLAN</dt><dd>{d.vlan ?? '—'}</dd>
        <dt>Status</dt><dd><StateBadge state={d.state} /></dd>
        <dt>Last seen</dt><dd>{ago(d.lastSeen)}</dd>
      </dl>
      <div className="buttons">
        {d.switchId ? <Link className="btn primary" to={`/switches/${d.switchId}?port=${d.ifIndex}`}>Open Switch</Link> : <button className="btn" disabled title="No edge location yet">Open Switch</button>}
        <Link className="btn" to={`/devices/${d.id}/trace`}>Trace Device</Link>
      </div>
    </div>
  );
}

export default function SearchPage() {
  const [sp] = useSearchParams(); const q = sp.get('q') ?? '';
  const { data, error, isLoading } = useQuery({ queryKey: ['search', q], queryFn: () => api.search(q), enabled: q.trim().length >= 2 });
  const none = data && !data.devices.length && !data.switches.length && !data.ports.length;
  return (
    <section>
      <h1>Search results for “{q}”</h1>
      <ErrorBox error={error} />
      {q.trim().length < 2 && <p className="muted">Type at least 2 characters. MAC addresses work in any format (AA:BB:CC:DD:EE:FF, aabb.ccdd.eeff, AA-BB-…), including fragments of 4+ hex characters.</p>}
      {isLoading && <p>Searching…</p>}
      {none && <p>No devices, switches or ports match. Devices appear here after the switches they connect to have been scanned.</p>}
      {!!data?.devices.length && <h2>Devices <span className="muted">({data.devices.length}{data.truncated ? '+' : ''})</span></h2>}
      {data?.devices.map((d) => <DeviceCard key={d.id} d={d} />)}
      {!!data?.switches.length && <h2>Switches</h2>}
      {data?.switches.map((s) => (
        <div className="result" key={s.id}>
          <div className="result-head"><span className="tag">SWITCH</span> <Link to={`/switches/${s.id}`}><strong>{s.name}</strong></Link> <span className="muted">{s.managementIp} · {s.manufacturerName}{s.model ? ` · ${s.model}` : ''}</span> <StatusBadge status={s.status} /></div>
          <div className="buttons"><Link className="btn primary" to={`/switches/${s.id}`}>Open Switch</Link></div>
        </div>
      ))}
      {!!data?.ports.length && <h2>Ports</h2>}
      {data?.ports.map((p) => (
        <div className="result" key={p.portId}>
          <div className="result-head"><span className="tag">PORT</span> <strong>{p.switchName}</strong> · {p.portName} <span className="muted">{p.alias ?? ''}</span> <PortTypeBadge type={p.type} /></div>
          <div className="buttons"><Link className="btn primary" to={`/switches/${p.switchId}?port=${p.ifIndex}`}>Open Switch</Link></div>
        </div>
      ))}
      {data?.truncated && <p className="muted">Showing the first results. Narrow the search to see more.</p>}
    </section>
  );
}
