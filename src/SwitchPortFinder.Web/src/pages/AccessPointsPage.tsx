import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { api } from '../api';
import { Dot, ErrorBox, IconLink, PageHead, RowMenu, ago } from '../components/Bits';
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
      <PageHead icon="M5 12.5a10 10 0 0 1 14 0M8.5 16a5 5 0 0 1 7 0M12 19.5h.01M2 9a14 14 0 0 1 20 0" title="Access points" subtitle={s ? `${s.total} access point${s.total === 1 ? '' : 's'} · ${s.online} online${s.offline ? ` · ${s.offline} offline` : ''}${s.wifiClients ? ` · ${s.wifiClients} Wi-Fi clients` : ''}` : 'Loading…'}>
        <Link className="btn" to="/switches/new?controller=1">+ Add Wireless Controller</Link>
      </PageHead>
      <div className="toolbar" style={{ gap: 10 }}>
        <input placeholder="Search name, IP, MAC, switch, port, VLAN" aria-label="Search access points" value={q} onChange={(e) => setQ(e.target.value)} style={{ flex: '1 1 300px', minWidth: 0 }} />
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
      <div className="cardlist-wrap">
        <table className="cardlist">
          <thead><tr><th>Access point</th><th>Address</th><th>Controller</th><th>Location</th><th title="Wi-Fi clients reported by the wireless controller, and devices found on the AP's switch port (not counting the AP). Click the number to list them.">Clients</th><th>Status</th><th><span className="sr-only">Actions</span></th></tr></thead>
          <tbody>
            {rows.map((i) => (
              <tr key={`${i.mac}-${i.deviceId ?? ''}`}>
                <td className="c-main"><div className="swname">{i.deviceId ? <Link className="rowlink" to={`/devices/${i.deviceId}`}>{i.name}</Link> : <strong>{i.name}</strong>}</div><div className="muted small">{i.model ?? i.vendor ?? '—'}{i.note ? ` · ${i.note}` : ''}</div></td>
                <td className="c-sub"><div>{i.ip ?? <span className="muted">Unknown IP</span>}</div><div className="mono muted small">{i.mac}</div></td>
                <td className="c-meta">{i.controller ?? <span className="muted">—</span>}</td>
                <td className="c-meta">{i.switchId ? <>
                  <div><Link to={`/switches/${i.switchId}`}>{i.switchName}</Link></div>
                  <div className="muted small">{i.ifIndex ? <Link to={`/switches/${i.switchId}?port=${i.ifIndex}`}>{i.portName}</Link> : '—'}{i.vlan != null && <span className="vlanchip">VLAN {i.vlan}</span>}</div></> : <span className="muted">Not located yet</span>}</td>
                <td className="c-meta">
                  <div>{i.wifiClients != null ? <><strong>{i.wifiClients}</strong> Wi-Fi</> : <span className="muted">—</span>}</div>
                  <div className="small">{i.devicesBehind > 0 && i.portId ? <Link to={`/devices?portId=${i.portId}${i.deviceId ? `&excludeId=${i.deviceId}` : ''}&behind=${encodeURIComponent(i.name)}`} title="Show the devices behind this access point">{i.devicesBehind} device{i.devicesBehind === 1 ? '' : 's'} behind</Link> : <span className="muted">0 behind</span>}</div>
                </td>
                <td className="c-status"><div className="statuscell"><Dot kind={i.online ? 'ok' : i.status === 'Disabled' ? 'muted' : 'warn'} label={i.status} /><div className="muted small">{i.uptimeSeconds != null ? `up ${Math.floor(i.uptimeSeconds / 86400)}d ${Math.floor((i.uptimeSeconds % 86400) / 3600)}h` : ago(i.lastSeen)}</div></div></td>
                <td className="rowtools"><div className="tools">
                  {i.deviceId && <IconLink to={`/devices/${i.deviceId}`} label={`View ${i.name}`} d="M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12zM12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6z" />}
                  <span className="more"><RowMenu label={`More actions for ${i.name}`}>{(close) => <>
                    {i.deviceId && <Link role="menuitem" to={`/devices/${i.deviceId}`} onClick={close}>View device</Link>}
                    {i.switchId && <Link role="menuitem" to={`/switches/${i.switchId}`} onClick={close}>Open switch</Link>}
                    {i.switchId && i.ifIndex ? <Link role="menuitem" to={`/switches/${i.switchId}?port=${i.ifIndex}`} onClick={close}>Open port</Link> : null}
                    <button type="button" role="menuitem" onClick={() => { close(); navigator.clipboard?.writeText(i.mac).catch(() => undefined); }}>Copy MAC</button>
                  </>}</RowMenu></span>
                </div></td>
              </tr>
            ))}
            {!isLoading && rows.length === 0 && <tr><td colSpan={7} className="empty-row">{s && s.total > 0 ? 'No access point matches the filters.' : 'No access points yet. Use “+ Add Wireless Controller” above (enter its IP and SNMP read community), then scan it, or they are detected from LLDP/CDP, host names and manufacturer rules.'}</td></tr>}
          </tbody>
        </table>
      </div>
      <p className="muted small" style={{ marginTop: 10 }}>“Devices behind” counts the devices found on the AP's switch port (click the number to list them). Wireless clients whose traffic the Cisco controller tunnels do not appear here.</p>
    </section>
  );
}
