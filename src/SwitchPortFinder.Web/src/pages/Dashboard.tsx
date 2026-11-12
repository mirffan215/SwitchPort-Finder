import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Link, useNavigate } from 'react-router-dom';
import { api, type Dashboard as DashboardDto } from '../api';
import { ErrorBox, StateDot, ago } from '../components/Bits';
import { useAuth } from '../components/Auth';
import { Donut } from '../components/Charts';
import { TopUptime, TopUtilization } from '../components/TopPortsCharts';

interface Attn { sev: 'bad' | 'warn' | 'muted'; title: string; detail: string; to: string; action: string }

export default function Dashboard() {
  const { canWrite } = useAuth();
  const nav = useNavigate();
  const [open, setOpen] = useState(false);
  const { data: d, error } = useQuery({ queryKey: ['dashboard'], queryFn: api.dashboard, refetchInterval: 15_000 });
  const alerts = useQuery({ queryKey: ['alerts'], queryFn: api.alerts, refetchInterval: 30_000 });
  const aps = useQuery({ queryKey: ['access-points'], queryFn: api.accessPoints });
  const sws = useQuery({ queryKey: ['switches'], queryFn: api.listSwitches });
  const util = useQuery({ queryKey: ['top-util', 10], queryFn: () => api.topPorts('utilization', 10), refetchInterval: 30_000 });
  const last = d?.lastDiscovery;
  const swList = sws.data ?? [];
  const stale = (s: { status: string; lastScanAt?: string | null }) => s.status === 'Reachable' && !!s.lastScanAt && Date.now() - new Date(s.lastScanAt).getTime() > 24 * 3600 * 1000;

  // Everything that needs a person, worst first. Built from data the portal already has.
  const attn: Attn[] = [];
  for (const a of alerts.data?.active ?? []) attn.push({ sev: 'bad', title: a.title, detail: `Alert opened ${ago(a.firstAt)}`, to: '/settings', action: 'Alerts' });
  for (const s of swList.filter((x) => x.status === 'Unreachable')) attn.push({ sev: 'bad', title: `${s.name} is unreachable`, detail: `No SNMP answer from ${s.managementIp}${s.lastScanAt ? ` · last scan ${ago(s.lastScanAt)}` : ''}`, to: `/switches/${s.id}`, action: 'Open' });
  const offAps = (aps.data?.items ?? []).filter((x) => !x.online && x.status === 'Offline');
  for (const ap of offAps.slice(0, 5)) attn.push({ sev: 'warn', title: `${ap.name} is offline`, detail: `${ap.controller ? `Not listed by ${ap.controller}` : 'Not found by its switch'}${ap.switchName ? ` · last on ${ap.switchName} ${ap.portName ?? ''}` : ''}`, to: ap.deviceId ? `/devices/${ap.deviceId}` : '/access-points', action: 'Open' });
  if (offAps.length > 5) attn.push({ sev: 'warn', title: `${offAps.length - 5} more access points are offline`, detail: 'See the full list', to: '/access-points', action: 'View all' });
  for (const p of (util.data?.items ?? []).filter((x) => (x.traffic.utilizationPct ?? 0) >= 80)) attn.push({ sev: 'warn', title: `${p.switchName} ${p.portName} is at ${Math.round(p.traffic.utilizationPct ?? 0)}%`, detail: 'Busier direction as a share of link speed', to: `/switches/${p.switchId}?port=${p.ifIndex}`, action: 'View port' });
  for (const s of swList.filter(stale)) attn.push({ sev: 'muted', title: `${s.name} has not been scanned for ${Math.floor((Date.now() - new Date(s.lastScanAt!).getTime()) / 86400000)} day(s)`, detail: 'Its devices may be out of date', to: `/switches/${s.id}`, action: 'Open' });
  const worst = attn.some((x) => x.sev === 'bad') ? 'bad' : attn.length ? 'warn' : 'ok';
  const reasons = [
    alerts.data?.activeCount ? `${alerts.data.activeCount} active alert${alerts.data.activeCount === 1 ? '' : 's'}` : '',
    swList.some((x) => x.status === 'Unreachable') ? `${swList.filter((x) => x.status === 'Unreachable').length} switch(es) unreachable` : '',
    offAps.length ? `${offAps.length} access point${offAps.length === 1 ? '' : 's'} offline` : '',
    (util.data?.items ?? []).some((x) => (x.traffic.utilizationPct ?? 0) >= 80) ? `${(util.data?.items ?? []).filter((x) => (x.traffic.utilizationPct ?? 0) >= 80).length} port(s) above 80%` : '',
    swList.some(stale) ? `${swList.filter(stale).length} switch(es) not scanned for a day` : '',
  ].filter(Boolean).join(' · ');

  const reachable = swList.filter((x) => x.status === 'Reachable').length; const unreachable = swList.filter((x) => x.status === 'Unreachable').length;
  const portsUp = d ? Math.max(0, d.totalPorts - d.downPorts) : 0;
  const hot = new Set((util.data?.items ?? []).filter((x) => (x.traffic.utilizationPct ?? 0) >= 80).map((x) => x.switchId));
  const fleetKind = (s: (typeof swList)[number]) => (s.status === 'Unreachable' ? 'bad' : s.status !== 'Reachable' || stale(s) ? 'muted' : hot.has(s.id) ? 'warn' : 'ok');

  return (
    <section className="dash">
      <div className="toolbar"><div><h1 style={{ margin: 0 }}>Network overview</h1><div className="muted small">{last ? `Last scan ${ago(last.completedAt ?? last.queuedAt)} · ` : ''}{swList.length} switch{swList.length === 1 ? '' : 'es'}{aps.data && aps.data.summary.total > 0 ? ` · ${aps.data.summary.total} access points` : ''}</div></div><div className="spacer" />{canWrite && <Link className="btn" to="/switches/new">+ Add Switch</Link>}</div>
      <ErrorBox error={error} />
      {d && d.switches === 0 && <div className="alert">No switches yet. <Link to="/switches/new">Add your first switch</Link>, <Link to="/switches/new?controller=1">add your wireless controller</Link> or <Link to="/settings">import a list</Link>.</div>}

      <div className={`dbanner ${worst}`} role="status">
        <span className={`pill ${worst}`}><span className="dot" aria-hidden="true" />{worst === 'ok' ? 'All good' : 'Needs attention'}</span>
        <span className="big">{worst === 'ok' ? 'Everything looks healthy' : `${attn.length} thing${attn.length === 1 ? '' : 's'} need${attn.length === 1 ? 's' : ''} you today`}</span>
        <span className="why">{worst === 'ok' ? (swList.length ? 'All switches reachable, no active alerts.' : '') : reasons}</span>
        <span className="spacer" />
        {attn.length > 0 && <button type="button" className="link" aria-expanded={open} onClick={() => setOpen(!open)}>{open ? 'Hide ↑' : 'Review ↓'}</button>}
      </div>
      {open && attn.length > 0 && (
        <ul className="att panelx" aria-label="Items that need attention">
          {attn.map((a, i) => (
            <li key={i}><span className={`bar ${a.sev}`} /><div><b>{a.title}</b><span className="m">{a.detail}</span></div><Link to={a.to}>{a.action}</Link></li>
          ))}
        </ul>
      )}

      <div className="dkpis">
        <Kpi label="Switches" n={reachable} of={swList.length} to="/switches" foot={unreachable > 0 ? <span className="down">{unreachable} unreachable</span> : <span className="up">all reachable</span>} warn={unreachable > 0} />
        <Kpi label="Devices online" n={d ? d.devices - d.offlineDevices : undefined} of={d?.devices} to="/devices" foot={<span className={d?.offlineDevices ? 'muted' : 'up'}>{d?.offlineDevices ?? 0} offline</span>} />
        <Kpi label="Access points" n={aps.data ? aps.data.summary.online : undefined} of={aps.data?.summary.total} to="/access-points" warn={(aps.data?.summary.offline ?? 0) > 0}
          foot={aps.data && aps.data.summary.total > 0 ? <><span className={aps.data.summary.offline ? 'down' : 'up'}>{aps.data.summary.offline} offline</span><span>{aps.data.summary.wifiClients ?? 0} Wi-Fi clients</span></> : <span className="muted">add the controller</span>} />
        <Kpi label="Ports in use" n={d && d.totalPorts ? Math.round((portsUp / d.totalPorts) * 100) : undefined} unit="%" to="/ports?status=up" foot={<><span>{portsUp} of {d?.totalPorts ?? 0} up</span><span className="mono">{d?.switchUplinks ?? 0} uplinks</span></>} />
        <Kpi label="Active alerts" n={alerts.data?.activeCount} to="/settings" warn={(alerts.data?.activeCount ?? 0) > 0} bad={(alerts.data?.activeCount ?? 0) > 0} foot={<span className={alerts.data?.activeCount ? 'down' : 'up'}>{alerts.data?.activeCount ? 'needs review' : 'none open'}</span>} />
        <div className="dkpi"><span className="label">Last scan</span><span className="val">{last ? ago(last.completedAt ?? last.queuedAt).replace(' ago', '') : '—'}</span><span className="foot">{last ? <span className={last.status === 'Failed' ? 'down' : last.status === 'CompletedWithWarnings' ? 'muted' : 'up'}>{last.switchName}: {last.status === 'CompletedWithWarnings' ? 'warnings' : last.status}</span> : <span className="muted">no scan yet</span>}</span></div>
      </div>

      <TopUtilization n={10} wide title="Busiest links" />

      {swList.length > 0 && (
        <div className="panelx">
          <h2>Switch fleet <Link className="small" to="/switches">view all</Link></h2>
          <p className="hint">One tile per switch, coloured by its worst signal.</p>
          <div className="fleet">
            {swList.map((s) => (
              <Link key={s.id} to={`/switches/${s.id}`} className={`sw ${fleetKind(s)}`}>
                <b>{s.name}</b>
                <span>{s.status === 'Unreachable' ? 'Unreachable' : s.status !== 'Reachable' ? 'Never scanned' : stale(s) ? `Not scanned ${ago(s.lastScanAt)}` : hot.has(s.id) ? 'Busy uplink' : `${s.portsUp ?? 0} / ${s.portCount} ports up`}</span>
              </Link>
            ))}
          </div>
          <div className="legend"><span><i className="ok" />Healthy</span><span><i className="warn" />Warning</span><span><i className="bad" />Down</span><span><i className="muted" />Stale or never scanned</span></div>
        </div>
      )}

      <div className="charts three">
        <TopUptime />
        <div className="panelx">
          <h2>Ports by state</h2>
          <p className="hint">Click a slice or legend row to list those ports.</p>
          {d && d.totalPorts > 0 ? <Donut centre="ports" caption="Ports by state" slices={portSlices(d, nav)} /> : <div className="empty">No ports yet. Scan a switch.</div>}
        </div>
        <div className="panelx">
          <h2>Devices</h2>
          <p className="hint">Online vs offline endpoints.</p>
          {d && d.devices > 0 ? <Donut centre="devices" caption="Devices by state" slices={[
            { label: 'Online', value: d.devices - d.offlineDevices, color: 'var(--ok)', onClick: () => nav('/devices') },
            { label: 'Offline', value: d.offlineDevices, color: 'var(--warn)', onClick: () => nav('/devices?state=Offline') }]} /> : <div className="empty">No devices yet.</div>}
        </div>
      </div>

      <h2>Recently discovered devices <Link className="small" to="/devices">all</Link></h2>
      <div className="tablewrap"><table>
        <thead><tr><th>Device</th><th>IP</th><th>Location</th><th>Status</th></tr></thead>
        <tbody>
          {d?.recentDevices.slice(0, 5).map((x) => <tr key={x.id}><td><div className="swname"><Link to={`/devices/${x.id}`}>{x.hostname}</Link></div><div className="muted small mono">{x.mac}</div></td><td>{x.ip ?? <span className="muted">Unknown</span>}</td>
            <td>{x.switchId ? <Link to={`/switches/${x.switchId}?port=${x.ifIndex}`}>{x.switchName} · {x.portName}</Link> : '—'}</td><td><StateDot state={x.state} lastSeen={x.lastSeen} /></td></tr>)}
          {d?.recentDevices.length === 0 && <tr><td colSpan={4} className="muted">No devices yet. Scan a switch.</td></tr>}
        </tbody></table></div>
    </section>
  );
}

function Kpi({ label, n, of, unit, to, foot, warn, bad }: { label: string; n?: number; of?: number; unit?: string; to: string; foot: React.ReactNode; warn?: boolean; bad?: boolean }) {
  return (
    <Link className={`dkpi ${bad ? 'bad' : warn ? 'warn' : ''}`} to={to}>
      <span className="label">{label}</span>
      <span className="val">{n ?? '—'}{unit && n != null && <small>{unit}</small>}{of != null && <small> / {of}</small>}</span>
      <span className="foot">{foot}</span>
    </Link>
  );
}

function portSlices(d: DashboardDto, nav: (to: string) => void) {
  const up = Math.max(0, d.totalPorts - d.downPorts); const uplinks = Math.min(d.switchUplinks, up); const endpoints = up - uplinks;
  return [
    { label: 'Connected', value: endpoints, color: 'var(--ok)', onClick: () => nav('/ports?status=up') },
    { label: 'Switch uplinks', value: uplinks, color: 'var(--accent)', onClick: () => nav('/ports?type=SwitchUplink') },
    { label: 'Down / unused', value: d.downPorts, color: 'var(--muted)', onClick: () => nav('/ports?status=down') }];
}
