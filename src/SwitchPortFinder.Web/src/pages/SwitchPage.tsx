import { useEffect, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import { api, type Neighbor, type PortRow } from '../api';
import { ConfidenceBadge, Dot, ErrorBox, PortTypeBadge, StateBadge, StatusBadge, TestResultBox, ago, fmtDate } from '../components/Bits';
import ScanProgress from '../components/ScanProgress';
import { useAuth } from '../components/Auth';
import { Donut, fmtBps, fmtRate } from '../components/Charts';

export default function SwitchPage() {
  const id = Number(useParams().id);
  const { canWrite } = useAuth();
  const [sp, setSp] = useSearchParams();
  const highlight = sp.get('port') ? Number(sp.get('port')) : undefined;
  const qc = useQueryClient();
  const sw = useQuery({ queryKey: ['switch', id], queryFn: () => api.getSwitch(id) });
  const ports = useQuery({ queryKey: ['ports', id], queryFn: () => api.ports(id) });
  const [jobId, setJobId] = useState<number | null>(null);
  const [scanning, setScanning] = useState(false);
  const [selected, setSelected] = useState<number | undefined>(highlight);
  const [view, setView] = useState<'table' | 'graphic'>(() => { try { return localStorage.getItem('spf.switchView') === 'table' ? 'table' : 'graphic'; } catch { return 'graphic'; } });
  const [heat, setHeat] = useState<'status' | 'util' | 'drops'>('status');
  const pickView = (v: 'table' | 'graphic') => { setView(v); try { localStorage.setItem('spf.switchView', v); } catch { /* optional */ } };

  const scan = useMutation({ mutationFn: () => api.scan(id), onSuccess: (r) => { setJobId(r.jobId); setScanning(true); } });
  const test = useMutation({ mutationFn: () => api.testSaved(id), onSuccess: () => qc.invalidateQueries({ queryKey: ['switch', id] }) });
  const refresh = () => { qc.invalidateQueries({ queryKey: ['switch', id] }); qc.invalidateQueries({ queryKey: ['ports', id] }); };

  // Open Switch from search/devices: ?port=<ifIndex> highlights, scrolls to and opens that port.
  useEffect(() => {
    if (highlight === undefined || !ports.data) return;
    setSelected(highlight);
    document.getElementById(`port-${highlight}`)?.scrollIntoView({ block: 'center' });
  }, [highlight, ports.data]);

  if (sw.isLoading) return <p>Loading…</p>;
  if (sw.error || !sw.data) return <ErrorBox error={sw.error ?? new Error('Switch not found')} />;
  const s = sw.data; const sum = ports.data?.summary;
  const sel = ports.data?.ports.find((p) => p.ifIndex === selected);

  return (
    <section>
      <div className="toolbar">
        <h1>{s.name}{s.isWirelessController && <span className="tag" title="Wireless controller" style={{ marginLeft: 8 }}>Wireless controller</span>}</h1>
        <span className="muted">· {s.managementIp} · {s.manufacturerName}{sum ? ` · ${sum.total} Ports · ${sum.connected} Connected · ${sum.down} Down · ${sum.uplinks} Switch Uplinks` : ''}</span>
        <StatusBadge status={s.status} />
        <div className="spacer" />
        <div className="viewsw" role="group" aria-label="View"><button type="button" aria-pressed={view === 'graphic'} onClick={() => pickView('graphic')}>Graphical</button><button type="button" aria-pressed={view === 'table'} onClick={() => pickView('table')}>Table</button></div>
        {canWrite && <button className="btn primary" disabled={scan.isPending || scanning} onClick={() => scan.mutate()}>Scan Now</button>}
        <button className="btn" onClick={refresh}>Refresh</button>
        {canWrite && <button className="btn" disabled={test.isPending} onClick={() => test.mutate()}>Test SNMP</button>}
        {canWrite && <Link className="btn" to={`/switches/${id}/edit`}>Edit</Link>}
      </div>
      <ErrorBox error={scan.error ?? test.error ?? ports.error} />
      {jobId && <ScanProgress jobId={jobId} onDone={() => { setScanning(false); refresh(); }} onClose={() => setJobId(null)} />}
      {test.data && <TestResultBox r={test.data} />}
      {s.vendorMismatch && <div className="alert warn">⚠ Selected manufacturer ({s.manufacturerName}) differs from the detected vendor.</div>}
      <p className="muted">Last scan: {fmtDate(s.lastScanAt)}{s.sysName ? ` · sysName ${s.sysName}` : ''}{s.model ? ` · ${s.model}` : ''}</p>
      {ports.data && !ports.data.hasArpSource && (
        <div className="alert warn">Mark your core / L3 switch as an ARP source (Edit → “Use as ARP source”) to see IP addresses.</div>
      )}

      {ports.isLoading ? (
        <p className="muted">Loading ports…</p>
      ) : !ports.data ? null : ports.data.ports.length === 0 ? (
        <p>No ports yet. Click <strong>Scan Now</strong> to discover ports, MAC addresses and IPs.</p>
      ) : (
        <>
        {view === 'graphic' && <Graphical ports={ports.data.ports} selected={selected} heat={heat} setHeat={setHeat}
          onPick={(p) => { setSelected(p.ifIndex); setSp({}, { replace: true }); }} />}
        <div className="split">
          {view === 'table' && <div className="tablewrap">
            <table>
              <thead><tr><th>Port</th><th>Status</th><th>Type</th><th>Device</th><th>IP</th><th>MAC</th><th>VLAN</th></tr></thead>
              <tbody>
                {ports.data?.ports.map((p) => {
                  const d = p.devices[0]; const more = p.macCount - (d ? 1 : 0);
                  const shared = p.type === 'Endpoint' && p.macCount >= 5;   // many MACs on one edge port: an access point / controller / hub, not one endpoint
                  const ap = shared && d?.category === 'AccessPoint' ? d : undefined;
                  const up = p.type === 'SwitchUplink' && p.neighbor;
                  return (
                    <tr key={p.portId} id={`port-${p.ifIndex}`} className={`clickable ${selected === p.ifIndex ? 'highlight' : ''} ${p.type === 'Down' ? 'dim' : ''}`}
                        onClick={() => { setSelected(p.ifIndex); setSp({}, { replace: true }); }}>
                      <td>{p.name}</td>
                      <td><Dot kind={p.status === 'UP' ? 'ok' : 'muted'} label={p.status === 'UP' ? 'Up' : 'Down'} /></td>
                      <td>{shared ? <span className="badge info" title="Many devices were learned on this port (an access point, wireless controller or unmanaged switch).">⇅ {ap ? 'Access point' : 'Shared'}</span> : <PortTypeBadge type={p.type} />}</td>
                      {up ? (
                        <td colSpan={3}><UplinkCell n={p.neighbor!} /></td>
                      ) : (<>
                        {shared ? <>
                          <td onClick={(e) => e.stopPropagation()}>{ap ? <>{ap.hostname === 'Unknown Device' ? 'Access point' : ap.hostname} · </> : null}<Link to={`/devices?portId=${p.portId}${ap ? `&excludeId=${ap.deviceId}` : ''}&behind=${encodeURIComponent(ap && ap.hostname !== 'Unknown Device' ? ap.hostname : p.name)}`} title="List the devices found on this port">{ap ? more : p.macCount} device{(ap ? more : p.macCount) === 1 ? '' : 's'} {ap ? 'behind' : 'on this port'}</Link></td>
                          <td>{ap?.ip ?? '—'}</td><td className="mono">{ap?.mac ?? '—'}</td>
                        </> : <>
                        <td>{p.type === 'Down' ? '—' : d ? <>{d.hostname}{more > 0 && <span className="muted"> (+{more} more)</span>}</> : '—'}</td>
                        <td>{p.type === 'Down' ? '—' : d?.ip ?? (d ? <span className="muted">Unknown</span> : '—')}</td>
                        <td className="mono">{p.type === 'Down' ? '—' : d?.mac ?? '—'}</td></>}
                      </>)}
                      <td>{p.vlan ?? d?.vlan ?? '—'}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>}
          {view === 'graphic' && !sel && <div className="panelx" style={{ flex: 1 }}><div className="empty">Click a port in the panel above to inspect it.</div></div>}
          {sel && <PortPanel sw={s.name} p={sel} onClose={() => setSelected(undefined)} />}
        </div>
        </>
      )}
    </section>
  );
}

function openTo(n: Neighbor) { return `/switches/${n.connectedSwitchId}${n.remotePortIfIndex ? `?port=${n.remotePortIfIndex}` : ''}`; }
const addTo = (n: Neighbor) => `/switches/new?name=${encodeURIComponent(n.remoteName ?? '')}&ip=${encodeURIComponent(n.remoteIp ?? '')}`;

/** Uplink rows show where the port leads — never the devices learned behind it. */
function UplinkCell({ n }: { n: Neighbor }) {
  return (
    <span onClick={(e) => e.stopPropagation()}>
      <strong>→ {n.connectedSwitchName ?? n.remoteName ?? 'Switch'}</strong>
      {(n.connectedSwitchIp ?? n.remoteIp) && <span className="muted"> {n.connectedSwitchIp ?? n.remoteIp}</span>}
      {n.remotePort && <span className="muted"> · {n.remotePort}</span>}{' '}
      <ConfidenceBadge c={n.confidence} />{' '}
      {n.connectedSwitchId ? <Link className="btn small" to={openTo(n)}>Open Switch</Link>
        : n.canAdd ? <Link className="btn small" to={addTo(n)}>Add this switch</Link> : <span className="muted">(unmanaged)</span>}
    </span>
  );
}

function PortPanel({ sw, p, onClose }: { sw: string; p: PortRow; onClose: () => void }) {
  return (
    <aside className="panel">
      <div className="toolbar"><h3>{sw} · {p.name}</h3><div className="spacer" /><button className="link" onClick={onClose}>✕</button></div>
      <dl className="kv">
        <dt>Description</dt><dd>{p.alias ?? '—'}</dd>
        <dt>Status</dt><dd>{p.status}</dd>
        <dt>Type</dt><dd><PortTypeBadge type={p.type} /></dd>
        <dt>Speed</dt><dd>{p.speedMbps ? `${p.speedMbps} Mbps` : '—'}</dd>
        <dt>VLAN</dt><dd>{p.vlan ?? '—'}</dd>
        <dt>Port MAC</dt><dd className="mono">{p.physAddress ?? '—'}</dd>
        <dt>MACs learned</dt><dd>{p.macCount}</dd>
      </dl>
      {p.traffic ? (
        <>
          <h4>Traffic <span className="muted small">(avg over {Math.max(1, Math.round(p.traffic.intervalSeconds / 60))} min, {ago(p.traffic.sampledAt)})</span></h4>
          <dl className="kv">
            <dt>Inbound</dt><dd>{fmtBps(p.traffic.inBps)}</dd><dt>Outbound</dt><dd>{fmtBps(p.traffic.outBps)}</dd>
            <dt>Utilization</dt><dd>{p.traffic.utilizationPct == null ? '—' : `${p.traffic.utilizationPct.toFixed(1)}%`}</dd>
            <dt>Drops / sec</dt><dd>{fmtRate(p.traffic.discardsPerSec)}</dd><dt>Errors / sec</dt><dd>{fmtRate(p.traffic.errorsPerSec)}</dd>
          </dl>
        </>
      ) : p.status === 'UP' ? <p className="muted small">Traffic rates appear after two scans.</p> : null}
      {p.neighbor && (
        <>
          <h4>{p.type === 'SwitchUplink' ? 'Connected switch' : 'Neighbor'}</h4>
          <dl className="kv">
            <dt>Switch</dt><dd>{p.neighbor.connectedSwitchName ?? p.neighbor.remoteName ?? '—'}</dd>
            <dt>Remote port</dt><dd>{p.neighbor.remotePort ?? '—'}</dd>
            <dt>Remote IP</dt><dd>{p.neighbor.connectedSwitchIp ?? p.neighbor.remoteIp ?? '—'}</dd>
            <dt>Confidence</dt><dd><ConfidenceBadge c={p.neighbor.confidence} /> <span className="muted">via {p.neighbor.protocol === 'Inferred' ? 'MAC table' : p.neighbor.protocol.toUpperCase()}</span></dd>
            {p.neighbor.capabilities && <><dt>Capabilities</dt><dd>{p.neighbor.capabilities}</dd></>}
            {p.neighbor.evidence && <><dt>Evidence</dt><dd className="muted">{p.neighbor.evidence}</dd></>}
          </dl>
          {p.neighbor.connectedSwitchId ? <Link className="btn primary" to={openTo(p.neighbor)}>Open Switch</Link>
            : p.neighbor.canAdd ? <Link className="btn primary" to={addTo(p.neighbor)}>Add this switch</Link> : null}
        </>
      )}
      {p.supporting?.length > 0 && (
        <>
          <h4>Supporting evidence</h4>
          <ul className="support">{p.supporting.map((x) => <li key={x}>{x}</li>)}</ul>
          <p className="muted small">Supporting signals never classify a port on their own.</p>
        </>
      )}
      {p.type === 'SwitchUplink' && <p className="muted">Devices learned behind an uplink are shown on the switch where they connect, not here.</p>}
      {p.devices.length > 0 && <h4>Devices</h4>}
      {p.devices.map((d) => (
        <dl className="kv devbox" key={d.deviceId}>
          <dt>Hostname</dt><dd>{d.hostname}</dd>
          <dt>IP</dt><dd>{d.ip ?? 'Unknown'}</dd>
          <dt>MAC</dt><dd className="mono">{d.mac}</dd>
          <dt>VLAN</dt><dd>{d.vlan ?? '—'}</dd>
          <dt>State</dt><dd><StateBadge state={d.state} /></dd>
          <dt>Last seen</dt><dd>{ago(d.lastSeen)}</dd>
          {d.lowConfidence && <><dt>Location</dt><dd className="warn">Lower confidence (MAC also seen on another switch)</dd></>}
        </dl>
      ))}
      {p.type !== 'SwitchUplink' && p.macCount > p.devices.length && <p className="muted">Showing {p.devices.length} of {p.macCount} devices.</p>}
    </aside>
  );
}

const levelColor = (pct: number) => (pct >= 85 ? 'var(--bad)' : pct >= 60 ? 'var(--warn)' : 'var(--ok)');

/** Graphical view: state donuts + a front-panel heat map. Colour by status, utilization or packet drops. */
function Graphical({ ports, selected, heat, setHeat, onPick }: { ports: PortRow[]; selected?: number; heat: 'status' | 'util' | 'drops'; setHeat: (h: 'status' | 'util' | 'drops') => void; onPick: (p: PortRow) => void }) {
  const up = ports.filter((p) => p.status === 'UP'); const uplinks = ports.filter((p) => p.type === 'SwitchUplink');
  const unknown = ports.filter((p) => p.type === 'Unknown');
  const endpoints = up.length - uplinks.length - unknown.length; const down = ports.length - up.length;
  const maxDrop = Math.max(...ports.map((p) => p.traffic?.discardsPerSec ?? 0), 0);
  const withTraffic = ports.filter((p) => p.traffic).length;
  const style = (p: PortRow): { cls: string; css?: React.CSSProperties; sub: string } => {
    if (heat === 'util') {
      const u = p.traffic?.utilizationPct;
      return u == null ? { cls: 'heat', sub: p.status === 'UP' ? 'no data' : 'down' } : { cls: 'heat', css: { borderColor: levelColor(u), background: `color-mix(in srgb, ${levelColor(u)} ${Math.max(10, u * 0.5)}%, var(--panel))` }, sub: `${u.toFixed(u < 10 ? 1 : 0)}%` };
    }
    if (heat === 'drops') {
      const d = p.traffic?.discardsPerSec ?? 0;
      return d > 0 && maxDrop > 0 ? { cls: 'heat', css: { borderColor: 'var(--bad)', background: `color-mix(in srgb, var(--bad) ${Math.max(12, (d / maxDrop) * 55)}%, var(--panel))` }, sub: `${fmtRate(d)}/s` } : { cls: 'heat', sub: p.traffic ? '0 drops' : 'no data' };
    }
    return { cls: p.type === 'Down' ? 'down' : p.type === 'SwitchUplink' ? 'uplink' : p.type === 'Unknown' ? 'unk' : 'up', sub: p.type === 'SwitchUplink' ? (p.neighbor?.connectedSwitchName ?? p.neighbor?.remoteName ?? 'Uplink') : p.type === 'Down' ? 'Unused' : p.devices[0]?.hostname ?? p.alias ?? '—' };
  };
  return (
    <div>
      <div className="charts three">
        <div className="panelx"><h2>Port state</h2><Donut centre="ports" caption="Port state" slices={[
          { label: 'Connected', value: Math.max(0, endpoints), color: 'var(--ok)' }, { label: 'Switch uplinks', value: uplinks.length, color: 'var(--accent)' },
          { label: 'Unknown', value: unknown.length, color: 'var(--warn)' }, { label: 'Down', value: down, color: 'var(--muted)' }]} /></div>
        <div className="panelx"><h2>Link speed</h2><Donut centre="up ports" caption="Link speed of connected ports" slices={speedSlices(up)} /></div>
        <div className="panelx"><h2>Traffic data</h2><p className="hint">{withTraffic} of {ports.length} ports have a rate. Rates need two scans.</p>
          {withTraffic > 0 && <Donut centre="ports" caption="Utilization" slices={utilSlices(ports)} />}</div>
      </div>
      <div className="toolbar"><h2 style={{ margin: 0 }}>Front panel</h2><span className="muted small">click a port to inspect</span><div className="spacer" />
        <div className="viewsw" role="group" aria-label="Colour ports by">
          {([['status', 'Status'], ['util', 'Utilization'], ['drops', 'Drops']] as const).map(([k, l]) => <button key={k} type="button" aria-pressed={heat === k} onClick={() => setHeat(k)}>{l}</button>)}
        </div></div>
      <div className="portgrid">
        {ports.map((p) => {
          const st = style(p);
          return (
            <button key={p.portId} type="button" className={`pbtn ${st.cls} ${selected === p.ifIndex ? 'sel' : ''}`} style={st.css} title={`${p.name} · ${p.status}`} onClick={() => onPick(p)}>
              <span className="pn"><span>{p.status}</span><span>{p.type === 'SwitchUplink' ? '⇅' : p.type === 'Down' ? '○' : '●'}</span></span>
              <span className="pi">{p.name}</span><span className="pd">{st.sub}</span>
            </button>
          );
        })}
      </div>
    </div>
  );
}

function speedSlices(up: PortRow[]) {
  const by = new Map<string, number>();
  for (const p of up) { const k = p.speedMbps >= 1000 ? `${p.speedMbps / 1000} Gbps` : p.speedMbps > 0 ? `${p.speedMbps} Mbps` : 'Unknown'; by.set(k, (by.get(k) ?? 0) + 1); }
  const colors = ['var(--accent)', 'var(--purple)', 'var(--ok)', 'var(--warn)', 'var(--bad)', 'var(--muted)'];
  return [...by.entries()].sort((a, b) => b[1] - a[1]).map(([label, value], i) => ({ label, value, color: colors[i % colors.length] }));
}

function utilSlices(ports: PortRow[]) {
  const u = ports.map((p) => p.traffic?.utilizationPct).filter((x): x is number => x != null);
  return [{ label: 'Under 60%', value: u.filter((x) => x < 60).length, color: 'var(--ok)' }, { label: '60–85%', value: u.filter((x) => x >= 60 && x < 85).length, color: 'var(--warn)' }, { label: 'Over 85%', value: u.filter((x) => x >= 85).length, color: 'var(--bad)' }];
}
