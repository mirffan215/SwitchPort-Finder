import { useQuery } from '@tanstack/react-query';
import { Link, useNavigate } from 'react-router-dom';
import { api, type Dashboard as DashboardDto } from '../api';
import { ErrorBox, StateBadge, StatusBadge, ago } from '../components/Bits';
import { useAuth } from '../components/Auth';
import { Donut } from '../components/Charts';
import { TopUptime, TopUtilization } from '../components/TopPortsCharts';

const BADGE: Record<string, string> = { Completed: 'ok', CompletedWithWarnings: 'warnb', Failed: 'bad', Running: 'info', Queued: 'muted' };

export default function Dashboard() {
  const { canWrite } = useAuth();
  const nav = useNavigate();
  const { data: d, error } = useQuery({ queryKey: ['dashboard'], queryFn: api.dashboard, refetchInterval: 15_000 });
  const alerts = useQuery({ queryKey: ['alerts'], queryFn: api.alerts, refetchInterval: 30_000 });
  const sws = useQuery({ queryKey: ['switches'], queryFn: api.listSwitches });
  const last = d?.lastDiscovery;
  return (
    <section>
      <div className="toolbar"><h1>Dashboard</h1><div className="spacer" />{canWrite && <Link className="btn" to="/switches/new">+ Add Switch</Link>}</div>
      <ErrorBox error={error} />
      <div className="cards">
        <Card n={d?.switches} label="Switches" to="/switches" />
        <Card n={d?.devices} label="Devices" to="/devices" />
        <Card n={d?.connectedPorts} label="Connected Ports" to="/ports?status=up" />
        <Card n={d?.switchUplinks} label="Switch Uplinks" to="/ports?type=SwitchUplink" />
        <Card n={d?.offlineDevices} label="Offline Devices" to="/devices?state=Offline" warn={!!d?.offlineDevices} />
        <div className="card"><div className="num sm">{last ? ago(last.completedAt ?? last.queuedAt) : '—'}</div><div>Last Discovery</div>{last && <small className={`badge ${BADGE[last.status]}`}>{last.switchName}: {last.status === 'CompletedWithWarnings' ? 'warnings' : last.status}</small>}</div>
      </div>
      {d && d.switches === 0 && <div className="alert">No switches yet. <Link to="/switches/new">Add your first switch</Link> or <Link to="/settings">import a list</Link>.</div>}
      {alerts.data && alerts.data.activeCount > 0 && (
        <div className="alert error" role="alert" style={{ margin: '0 0 14px' }}>
          <strong>{alerts.data.activeCount} active alert{alerts.data.activeCount === 1 ? '' : 's'}</strong>
          <ul style={{ margin: '6px 0 0 18px', padding: 0 }}>{alerts.data.active.slice(0, 5).map((a) => <li key={a.id}>{a.title} <span className="muted">· {ago(a.firstAt)}</span></li>)}</ul>
          {alerts.data.activeCount > 5 && <div className="muted small">…and {alerts.data.activeCount - 5} more (Settings → Notifications).</div>}
        </div>
      )}
      <div className="charts">
        <TopUtilization />
        <div className="panelx">
          <h2>Ports by state</h2>
          <p className="hint">Click a slice or legend row to list those ports.</p>
          {d && d.totalPorts > 0 ? <Donut centre="ports" caption="Ports by state" slices={portSlices(d, nav)} /> : <div className="empty">No ports yet. Scan a switch.</div>}
        </div>
      </div>
      <div className="charts">
        <TopUptime />
        <div className="panelx">
          <h2>Devices</h2>
          <p className="hint">Online vs offline endpoints.</p>
          {d && d.devices > 0 ? <Donut centre="devices" caption="Devices by state" slices={[
            { label: 'Online', value: d.devices - d.offlineDevices, color: 'var(--ok)', onClick: () => nav('/devices') },
            { label: 'Offline', value: d.offlineDevices, color: 'var(--warn)', onClick: () => nav('/devices?state=Offline') }]} /> : <div className="empty">No devices yet.</div>}
        </div>
      </div>
      {sws.data && sws.data.length > 0 && (<>
        <h2>Switch Health Overview <Link className="small" to="/switches">view all</Link></h2>
        <div className="health">
          {sws.data.slice(0, 8).map((x) => (
            <Link key={x.id} className="hcard" to={`/switches/${x.id}`}>
              <div className="top"><span>{x.name}</span><StatusBadge status={x.status} /></div>
              <div className="sub">{x.manufacturerName}{x.model ? ` ${x.model}` : ''}{x.location ? ` · ${x.location}` : ''}</div>
              <div className="meter"><span>Ports</span><b>{x.portCount}</b></div>
              <div className="meter"><span>Last scan</span><b>{ago(x.lastScanAt)}</b></div>
            </Link>
          ))}
        </div>
      </>)}
      <h2>Recently Discovered Devices <Link className="small" to="/devices">all</Link></h2>
      <div className="tablewrap"><table>
        <thead><tr><th>Device</th><th>IP</th><th>Location</th><th>Status</th></tr></thead>
        <tbody>
          {d?.recentDevices.slice(0, 5).map((x) => <tr key={x.id}><td><Link to={`/devices/${x.id}`}>{x.hostname}</Link><div className="muted small mono">{x.mac}</div></td><td>{x.ip ?? <span className="muted">Unknown</span>}</td>
            <td>{x.switchId ? <Link to={`/switches/${x.switchId}?port=${x.ifIndex}`}>{x.switchName} · {x.portName}</Link> : '—'}</td><td><StateBadge state={x.state} /></td></tr>)}
          {d?.recentDevices.length === 0 && <tr><td colSpan={4} className="muted">No devices yet. Scan a switch.</td></tr>}
        </tbody></table></div>
    </section>
  );
}

function portSlices(d: DashboardDto, nav: (to: string) => void) {
  const up = Math.max(0, d.totalPorts - d.downPorts); const uplinks = Math.min(d.switchUplinks, up); const endpoints = up - uplinks;
  return [
    { label: 'Connected', value: endpoints, color: 'var(--ok)', onClick: () => nav('/ports?status=up') },
    { label: 'Switch uplinks', value: uplinks, color: 'var(--accent)', onClick: () => nav('/ports?type=SwitchUplink') },
    { label: 'Down / unused', value: d.downPorts, color: 'var(--muted)', onClick: () => nav('/ports?status=down') }];
}

function Card({ n, label, to, warn }: { n?: number; label: string; to?: string; warn?: boolean }) {
  const body = <><div className={`num ${warn ? 'warn' : ''}`}>{n ?? '—'}</div><div>{label}</div></>;
  return to ? <Link className="card link" to={to}>{body}</Link> : <div className="card">{body}</div>;
}
