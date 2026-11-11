import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { api } from '../api';
import { ErrorBox, StateBadge, ago } from '../components/Bits';
import { Donut } from '../components/Charts';

/** Wireless access points: the devices the category rules (or an administrator) marked as access points. */
export default function AccessPointsPage() {
  const { data, error, isLoading } = useQuery({ queryKey: ['access-points'], queryFn: api.accessPoints });
  const [q, setQ] = useState(''); const [show, setShow] = useState<'all' | 'online' | 'offline'>('all'); const [sw, setSw] = useState('');
  const s = data?.summary;
  const rows = (data?.items ?? []).filter((i) => {
    const d = i.device; const text = `${d.hostname} ${d.ip ?? ''} ${d.mac} ${d.vendor ?? ''} ${d.switchName ?? ''} ${d.portName ?? ''} ${d.vlan ?? ''}`.toLowerCase();
    return (!q || text.includes(q.toLowerCase())) && (show === 'all' || (show === 'online') === (d.state === 'Active')) && (!sw || (d.switchName ?? 'Not located') === sw);
  });
  return (
    <section>
      <div className="toolbar"><h1>Access points</h1><div className="spacer" />
        <input placeholder="Search name, IP, MAC, switch, port, VLAN" aria-label="Search access points" value={q} onChange={(e) => setQ(e.target.value)} style={{ minWidth: 300 }} />
        <select value={show} onChange={(e) => setShow(e.target.value as typeof show)} aria-label="Status"><option value="all">All statuses</option><option value="online">Online</option><option value="offline">Offline</option></select>
        <select value={sw} onChange={(e) => setSw(e.target.value)} aria-label="Switch"><option value="">All switches</option>{s?.bySwitch.map((k) => <option key={k.key} value={k.key}>{k.key} ({k.value})</option>)}</select>
      </div>
      <ErrorBox error={error} />
      {s && s.total > 0 && (
        <div className="charts" style={{ marginBottom: 16 }}>
          <div className="panelx"><h2>Status</h2><Donut centre="access points" caption="Access points by status" slices={[{ label: 'Online', value: s.online, color: 'var(--ok)', onClick: () => setShow('online') }, { label: 'Offline', value: s.offline, color: 'var(--warn)', onClick: () => setShow('offline') }]} />
            <p className="hint" style={{ marginTop: 8 }}>Online = found by the latest scan of its switch.</p></div>
          <div className="panelx"><h2>By switch</h2>
            <ul className="hbars">{s.bySwitch.map((k) => <li key={k.key}><button type="button" className={sw === k.key ? 'sel' : ''} onClick={() => setSw(sw === k.key ? '' : k.key)}><span className="hb-name">{k.key}</span><span className="hb-track"><i style={{ width: `${(k.value / s.bySwitch[0].value) * 100}%`, background: 'var(--accent)' }} /></span><span className="hb-val">{k.value}</span></button></li>)}</ul>
            <p className="hint" style={{ marginTop: 8 }}>Manufacturers: {s.byManufacturer.map((m) => `${m.key} (${m.value})`).join(', ')}</p></div>
        </div>
      )}
      <div className="tablewrap">
        <table>
          <thead><tr><th>Access point</th><th>IP</th><th>MAC</th><th>Manufacturer</th><th>Switch</th><th>Port</th><th>VLAN</th><th>Status</th><th title="Devices found on the AP's switch port, not counting the AP itself. Click a number to see them.">Devices behind</th><th>Last seen</th></tr></thead>
          <tbody>
            {rows.map(({ device: d, clientsBehind, neighborInfo }) => (
              <tr key={d.id}>
                <td><Link to={`/devices/${d.id}`}>{d.hostname === 'Unknown Device' ? (neighborInfo ? neighborInfo.split(' · ')[0] : `${(d.vendor ?? 'Unnamed').replace(/,?\s*(Inc\.?|Co\.?,?\s*Ltd\.?|LTD|Networks).*/i, '')} access point`) : d.hostname}</Link>{d.categoryManual && <span className="tag" title="Type set manually">manual</span>}{neighborInfo && <div className="muted small">{neighborInfo}</div>}</td>
                <td>{d.ip ?? <span className="muted">Unknown</span>}</td><td className="mono">{d.mac}</td><td>{d.vendor ?? '—'}</td>
                <td>{d.switchId ? <Link to={`/switches/${d.switchId}`}>{d.switchName}</Link> : '—'}</td>
                <td>{d.switchId && d.ifIndex ? <Link to={`/switches/${d.switchId}?port=${d.ifIndex}`}>{d.portName}</Link> : '—'}</td>
                <td>{d.vlan ?? '—'}</td><td><StateBadge state={d.state} /></td><td>{clientsBehind > 0 && d.portId ? <Link to={`/devices?portId=${d.portId}&excludeId=${d.id}&behind=${encodeURIComponent(d.hostname === 'Unknown Device' ? (neighborInfo?.split(' · ')[0] ?? d.mac) : d.hostname)}`} title="Show the devices behind this access point">{clientsBehind} device{clientsBehind === 1 ? '' : 's'}</Link> : <span className="muted">0</span>}</td><td>{ago(d.lastSeen)}</td>
              </tr>
            ))}
            {!isLoading && rows.length === 0 && <tr><td colSpan={10} className="muted">{s && s.total > 0 ? 'No access point matches the filters.' : 'No access points yet. They are detected after a scan, from LLDP/CDP information, host names, VLAN 50 and manufacturer (rules under Settings → Device types). You can also set the type of any device by hand.'}</td></tr>}
          </tbody>
        </table>
      </div>
      <p className="muted small" style={{ marginTop: 10 }}>“Devices behind” counts the devices found on the AP's switch port (click the number to list them). Wireless clients whose traffic the Cisco controller tunnels do not appear here.</p>
    </section>
  );
}
