import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { api } from '../api';
import { ErrorBox, StateBadge, ago } from '../components/Bits';
import { useAuth } from '../components/Auth';

const BADGE: Record<string, string> = { Completed: 'ok', CompletedWithWarnings: 'warnb', Failed: 'bad', Running: 'info', Queued: 'muted' };

export default function Dashboard() {
  const { canWrite } = useAuth();
  const { data: d, error } = useQuery({ queryKey: ['dashboard'], queryFn: api.dashboard, refetchInterval: 15_000 });
  const last = d?.lastDiscovery;
  return (
    <section>
      <div className="toolbar"><h1>Dashboard</h1><div className="spacer" />{canWrite && <Link className="btn" to="/switches/new">+ Add Switch</Link>}</div>
      <ErrorBox error={error} />
      <div className="cards">
        <Card n={d?.switches} label="Switches" to="/switches" />
        <Card n={d?.devices} label="Devices" to="/devices" />
        <Card n={d?.connectedPorts} label="Connected Ports" />
        <Card n={d?.switchUplinks} label="Switch Uplinks" to="/topology" />
        <Card n={d?.offlineDevices} label="Offline Devices" to="/devices?state=Offline" warn={!!d?.offlineDevices} />
        <div className="card"><div className="num sm">{last ? ago(last.completedAt ?? last.queuedAt) : '—'}</div><div>Last Discovery</div>{last && <small className={`badge ${BADGE[last.status]}`}>{last.switchName}: {last.status === 'CompletedWithWarnings' ? 'warnings' : last.status}</small>}</div>
      </div>
      {d && d.switches === 0 && <div className="alert">No switches yet. <Link to="/switches/new">Add your first switch</Link> or <Link to="/settings">import a list</Link>.</div>}
      <div className="two">
        <div>
          <h2>Recent Discovery Jobs <Link className="small" to="/discovery">all</Link></h2>
          <div className="tablewrap"><table>
            <thead><tr><th>Switch</th><th>When</th><th>Status</th><th>Devices</th></tr></thead>
            <tbody>
              {d?.recentJobs.map((j) => <tr key={j.id}><td>{j.switchName}</td><td>{ago(j.completedAt ?? j.queuedAt)}</td><td><span className={`badge ${BADGE[j.status]}`}>{j.status === 'CompletedWithWarnings' ? 'warnings' : j.status}</span></td><td>{j.devicesFound}</td></tr>)}
              {d?.recentJobs.length === 0 && <tr><td colSpan={4} className="muted">No jobs yet.</td></tr>}
            </tbody></table></div>
        </div>
        <div>
          <h2>Recently Discovered Devices <Link className="small" to="/devices">all</Link></h2>
          <div className="tablewrap"><table>
            <thead><tr><th>Device</th><th>IP</th><th>Location</th><th>Status</th></tr></thead>
            <tbody>
              {d?.recentDevices.map((x) => <tr key={x.id}><td><Link to={`/devices/${x.id}`}>{x.hostname}</Link><div className="muted small mono">{x.mac}</div></td><td>{x.ip ?? <span className="muted">Unknown</span>}</td>
                <td>{x.switchId ? <Link to={`/switches/${x.switchId}?port=${x.ifIndex}`}>{x.switchName} · {x.portName}</Link> : '—'}</td><td><StateBadge state={x.state} /></td></tr>)}
              {d?.recentDevices.length === 0 && <tr><td colSpan={4} className="muted">No devices yet. Scan a switch.</td></tr>}
            </tbody></table></div>
        </div>
      </div>
    </section>
  );
}

function Card({ n, label, to, warn }: { n?: number; label: string; to?: string; warn?: boolean }) {
  const body = <><div className={`num ${warn ? 'warn' : ''}`}>{n ?? '—'}</div><div>{label}</div></>;
  return to ? <Link className="card link" to={to}>{body}</Link> : <div className="card">{body}</div>;
}
