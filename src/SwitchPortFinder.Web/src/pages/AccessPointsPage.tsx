import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { api } from '../api';
import { ErrorBox, ago } from '../components/Bits';
import { Donut } from '../components/Charts';

/** Wireless access points: the devices the category rules (or an administrator) marked as access points. */
export default function AccessPointsPage() {
  const { data, error, isLoading } = useQuery({ queryKey: ['access-points'], queryFn: api.accessPoints });
  const [q, setQ] = useState(''); const [show, setShow] = useState<'all' | 'online' | 'offline'>('all'); const [sw, setSw] = useState('');
  const s = data?.summary;
  const rows = (data?.items ?? []).filter((i) => {
    const text = `${i.name} ${i.ip ?? ''} ${i.mac} ${i.vendor ?? ''} ${i.model ?? ''} ${i.controller ?? ''} ${i.switchName ?? ''} ${i.portName ?? ''} ${i.vlan ?? ''}`.toLowerCase();
    return (!q || text.includes(q.toLowerCase())) && (show === 'all' || (show === 'online') === i.online) && (!sw || (i.switchName ?? 'Not located') === sw);
  });
  return (
    <section>
      <div className="toolbar"><h1>Access points</h1><div className="spacer" /><Link className="btn" to="/switches/new?controller=1">+ Add Wireless Controller</Link>
        <input placeholder="Search name, IP, MAC, switch, port, VLAN" aria-label="Search access points" value={q} onChange={(e) => setQ(e.target.value)} style={{ minWidth: 300 }} />
        <select value={show} onChange={(e) => setShow(e.target.value as typeof show)} aria-label="Status"><option value="all">All statuses</option><option value="online">Online</option><option value="offline">Offline</option></select>
        <select value={sw} onChange={(e) => setSw(e.target.value)} aria-label="Switch"><option value="">All switches</option>{s?.bySwitch.map((k) => <option key={k.key} value={k.key}>{k.key} ({k.value})</option>)}</select>
      </div>
      <ErrorBox error={error} />
      {s && s.total > 0 && (
        <div className="charts" style={{ marginBottom: 16 }}>
          <div className="panelx"><h2>Status</h2><Donut centre="access points" caption="Access points by status" slices={[{ label: 'Online', value: s.online, color: 'var(--ok)', onClick: () => setShow('online') }, { label: 'Offline', value: s.offline, color: 'var(--warn)', onClick: () => setShow('offline') }]} />
            <p className="hint" style={{ marginTop: 8 }}>Online = joined to the controller, or found by the latest scan of its switch.</p></div>
          <div className="panelx"><h2>By switch</h2>
            <ul className="hbars">{s.bySwitch.map((k) => <li key={k.key}><button type="button" className={sw === k.key ? 'sel' : ''} onClick={() => setSw(sw === k.key ? '' : k.key)}><span className="hb-name">{k.key}</span><span className="hb-track"><i style={{ width: `${(k.value / s.bySwitch[0].value) * 100}%`, background: 'var(--accent)' }} /></span><span className="hb-val">{k.value}</span></button></li>)}</ul>
            <p className="hint" style={{ marginTop: 8 }}>Manufacturers: {s.byManufacturer.map((m) => `${m.key} (${m.value})`).join(', ')}</p></div>
        </div>
      )}
      <div className="tablewrap">
        <table>
          <thead><tr><th>Access point</th><th>IP</th><th>MAC</th><th>Model</th><th>Controller</th><th>Switch</th><th>Port</th><th>VLAN</th><th>Status</th><th title="Wi-Fi clients reported by the wireless controller">Wi-Fi clients</th><th title="Devices found on the AP's switch port, not counting the AP itself. Click a number to see them.">Devices behind</th><th>Uptime</th><th>Last seen</th></tr></thead>
          <tbody>
            {rows.map((i) => (
              <tr key={`${i.mac}-${i.deviceId ?? ''}`}>
                <td>{i.deviceId ? <Link to={`/devices/${i.deviceId}`}>{i.name}</Link> : i.name}{i.note && <div className="muted small">{i.note}</div>}</td>
                <td>{i.ip ?? <span className="muted">Unknown</span>}</td><td className="mono">{i.mac}</td><td>{i.model ?? i.vendor ?? '—'}</td><td>{i.controller ?? '—'}</td>
                <td>{i.switchId ? <Link to={`/switches/${i.switchId}`}>{i.switchName}</Link> : '—'}</td>
                <td>{i.switchId && i.ifIndex ? <Link to={`/switches/${i.switchId}?port=${i.ifIndex}`}>{i.portName}</Link> : '—'}</td>
                <td>{i.vlan ?? '—'}</td><td><span className={`badge ${i.online ? 'ok' : 'warnb'}`}>{i.status}</span></td>
                <td>{i.wifiClients ?? '—'}</td>
                <td>{i.devicesBehind > 0 && i.portId ? <Link to={`/devices?portId=${i.portId}${i.deviceId ? `&excludeId=${i.deviceId}` : ''}&behind=${encodeURIComponent(i.name)}`} title="Show the devices behind this access point">{i.devicesBehind} device{i.devicesBehind === 1 ? '' : 's'}</Link> : <span className="muted">0</span>}</td>
                <td>{i.uptimeSeconds != null ? `${Math.floor(i.uptimeSeconds / 86400)}d ${Math.floor((i.uptimeSeconds % 86400) / 3600)}h` : '—'}</td><td>{ago(i.lastSeen)}</td>
              </tr>
            ))}
            {!isLoading && rows.length === 0 && <tr><td colSpan={13} className="muted">{s && s.total > 0 ? 'No access point matches the filters.' : 'No access points yet. Use “+ Add Wireless Controller” above (enter its IP and SNMP read community), then scan it, or they are detected from LLDP/CDP, host names and manufacturer rules.'}</td></tr>}
          </tbody>
        </table>
      </div>
      <p className="muted small" style={{ marginTop: 10 }}>“Devices behind” counts the devices found on the AP's switch port (click the number to list them). Wireless clients whose traffic the Cisco controller tunnels do not appear here.</p>
    </section>
  );
}
