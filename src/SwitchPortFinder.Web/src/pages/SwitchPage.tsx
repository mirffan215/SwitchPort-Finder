import { useEffect, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import { api, type Neighbor, type PortRow } from '../api';
import { ConfidenceBadge, ErrorBox, PortTypeBadge, StateBadge, StatusBadge, TestResultBox, ago, fmtDate } from '../components/Bits';
import ScanProgress from '../components/ScanProgress';

export default function SwitchPage() {
  const id = Number(useParams().id);
  const [sp, setSp] = useSearchParams();
  const highlight = sp.get('port') ? Number(sp.get('port')) : undefined;
  const qc = useQueryClient();
  const sw = useQuery({ queryKey: ['switch', id], queryFn: () => api.getSwitch(id) });
  const ports = useQuery({ queryKey: ['ports', id], queryFn: () => api.ports(id) });
  const [jobId, setJobId] = useState<number | null>(null);
  const [scanning, setScanning] = useState(false);
  const [selected, setSelected] = useState<number | undefined>(highlight);

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
        <h1>{s.name}</h1>
        <span className="muted">· {s.managementIp} · {s.manufacturerName}{sum ? ` · ${sum.total} Ports · ${sum.connected} Connected · ${sum.down} Down · ${sum.uplinks} Switch Uplinks` : ''}</span>
        <StatusBadge status={s.status} />
        <div className="spacer" />
        <button className="btn primary" disabled={scan.isPending || scanning} onClick={() => scan.mutate()}>Scan Now</button>
        <button className="btn" onClick={refresh}>Refresh</button>
        <button className="btn" disabled={test.isPending} onClick={() => test.mutate()}>Test SNMP</button>
        <Link className="btn" to={`/switches/${id}/edit`}>Edit</Link>
      </div>
      <ErrorBox error={scan.error ?? test.error ?? ports.error} />
      {jobId && <ScanProgress jobId={jobId} onDone={() => { setScanning(false); refresh(); }} onClose={() => setJobId(null)} />}
      {test.data && <TestResultBox r={test.data} />}
      {s.vendorMismatch && <div className="alert warn">⚠ Selected manufacturer ({s.manufacturerName}) differs from the detected vendor.</div>}
      <p className="muted">Last scan: {fmtDate(s.lastScanAt)}{s.sysName ? ` · sysName ${s.sysName}` : ''}{s.model ? ` · ${s.model}` : ''}</p>
      {ports.data && !ports.data.hasArpSource && (
        <div className="alert warn">Mark your core / L3 switch as an ARP source (Edit → “Use as ARP source”) to see IP addresses.</div>
      )}

      {ports.data && ports.data.ports.length === 0 ? (
        <p>No ports yet. Click <strong>Scan Now</strong> to discover ports, MAC addresses and IPs.</p>
      ) : (
        <div className="split">
          <div className="tablewrap">
            <table>
              <thead><tr><th>Port</th><th>Status</th><th>Type</th><th>Device</th><th>IP</th><th>MAC</th><th>VLAN</th></tr></thead>
              <tbody>
                {ports.data?.ports.map((p) => {
                  const d = p.devices[0]; const more = p.macCount - (d ? 1 : 0);
                  const up = p.type === 'SwitchUplink' && p.neighbor;
                  return (
                    <tr key={p.portId} id={`port-${p.ifIndex}`} className={`clickable ${selected === p.ifIndex ? 'highlight' : ''} ${p.type === 'Down' ? 'dim' : ''}`}
                        onClick={() => { setSelected(p.ifIndex); setSp({}, { replace: true }); }}>
                      <td>{p.name}</td>
                      <td><span className={p.status === 'UP' ? 'up' : 'down'}>{p.status}</span></td>
                      <td><PortTypeBadge type={p.type} /></td>
                      {up ? (
                        <td colSpan={3}><UplinkCell n={p.neighbor!} /></td>
                      ) : (<>
                        <td>{p.type === 'Down' ? '—' : d ? <>{d.hostname}{more > 0 && <span className="muted"> (+{more} more)</span>}</> : '—'}</td>
                        <td>{p.type === 'Down' ? '—' : d?.ip ?? (d ? <span className="muted">Unknown</span> : '—')}</td>
                        <td className="mono">{p.type === 'Down' ? '—' : d?.mac ?? '—'}</td>
                      </>)}
                      <td>{p.vlan ?? d?.vlan ?? '—'}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
          {sel && <PortPanel sw={s.name} p={sel} onClose={() => setSelected(undefined)} />}
        </div>
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
