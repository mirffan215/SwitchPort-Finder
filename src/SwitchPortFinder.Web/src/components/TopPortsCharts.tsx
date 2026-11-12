import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { api, type TopPort } from '../api';
import { ago } from './Bits';
import { Gauge, fmtBps, fmtRate, fmtUptime } from './Charts';

const WAIT = 'No traffic data yet. Rates are averaged between two scans, so they appear after the second scan of each switch.';

function Detail({ p }: { p: TopPort }) {
  const t = p.traffic;
  return (
    <div className="detail" aria-live="polite">
      <div><span className="lbl">Port</span><b>{p.switchName} · {p.portName}</b></div>
      <div><span className="lbl">Inbound</span><b>{fmtBps(t.inBps)}</b></div>
      <div><span className="lbl">Outbound</span><b>{fmtBps(t.outBps)}</b></div>
      <div><span className="lbl">Link speed</span><b>{p.speedMbps ? `${p.speedMbps} Mbps` : '—'}</b></div>
      <div><span className="lbl">Drops / sec</span><b>{fmtRate(t.discardsPerSec)}</b></div>
      <div><span className="lbl">Errors / sec</span><b>{fmtRate(t.errorsPerSec)}</b></div>
      <div><span className="lbl">Average over</span><b>{Math.round(t.intervalSeconds / 60) || '<1'} min · {ago(t.sampledAt)}</b></div>
      {!t.wide && <div><span className="lbl">Counters</span><b>32-bit (approximate)</b></div>}
      <div><Link className="btn small" to={`/switches/${p.switchId}?port=${p.ifIndex}`}>Open port</Link></div>
    </div>
  );
}

export function TopUtilization({ n = 5, title, wide }: { n?: number; title?: string; wide?: boolean }) {
  const { data } = useQuery({ queryKey: ['top-util', n], queryFn: () => api.topPorts('utilization', n), refetchInterval: 30_000 });
  const [sel, setSel] = useState<number | null>(null);
  const items = data?.items ?? []; const cur = items.find((i) => i.portId === sel) ?? null;
  return (
    <div className="panelx">
      <h2>{title ?? 'Top 5 port utilization'}</h2>
      <p className="hint">Busier direction as % of link speed, averaged between the last two scans. Click a gauge for details.</p>
      {items.length === 0 ? <div className="empty">{data && data.portsWithData > 0 ? 'No ports with a known link speed yet.' : WAIT}</div> : (
        <div className={`gauges ${wide ? 'wide' : ''}`}>
          {items.map((p) => <Gauge key={p.portId} pct={p.traffic.utilizationPct ?? 0} label={p.portName} sub={p.switchName} selected={sel === p.portId}
            onSelect={() => setSel(sel === p.portId ? null : p.portId)} title={`${p.switchName} ${p.portName}: in ${fmtBps(p.traffic.inBps)}, out ${fmtBps(p.traffic.outBps)}`} />)}
        </div>
      )}
      {cur && <Detail p={cur} />}
    </div>
  );
}

const GAUGE_FULL_DAYS = 365;   // the gauge is full after a year without a restart

/** Uptime of up to 5 switches (core first) with the same gauge as the utilization panel. */
export function TopUptime() {
  const { data } = useQuery({ queryKey: ['switch-uptime'], queryFn: api.switchUptime, refetchInterval: 30_000 });
  const [sel, setSel] = useState<number | null>(null);
  const items = data?.items ?? []; const cur = items.find((i) => i.switchId === sel) ?? null;
  const tone = (sec?: number | null): 'ok' | 'warn' | 'bad' => (sec == null ? 'warn' : sec < 86400 ? 'bad' : sec < 7 * 86400 ? 'warn' : 'ok');
  return (
    <div className="panelx">
      <h2>Switch uptime</h2>
      <p className="hint">Core switches first, then the most recently restarted. Red = restarted today, amber = within a week. The gauge is full at {GAUGE_FULL_DAYS} days. Click a gauge for details.</p>
      {items.length === 0 ? <div className="empty">{data && data.switchesTotal > 0 ? 'Uptime is read during a scan. Scan a switch to see it here.' : 'No switches yet.'}</div> : (
        <div className="gauges">
          {items.map((s) => <Gauge key={s.switchId} pct={s.uptimeSeconds == null ? 0 : Math.max(3, (s.uptimeSeconds / 86400 / GAUGE_FULL_DAYS) * 100)} text={s.uptimeSeconds == null ? 'n/a' : fmtUptime(s.uptimeSeconds)}
            tone={tone(s.uptimeSeconds)} label={s.name} sub={s.isCore ? 'Core switch' : s.model ?? s.managementIp} selected={sel === s.switchId}
            onSelect={() => setSel(sel === s.switchId ? null : s.switchId)} title={`${s.name}: ${s.uptimeSeconds == null ? 'unreachable, uptime unknown' : `up ${fmtUptime(s.uptimeSeconds)}`}`} />)}
        </div>
      )}
      {cur && (
        <div className="detail" aria-live="polite">
          <div><span className="lbl">Switch</span><b>{cur.name}{cur.isCore ? ' (core)' : ''}</b></div>
          <div><span className="lbl">IP address</span><b>{cur.managementIp}</b></div>
          <div><span className="lbl">Model</span><b>{cur.model ?? '—'}</b></div>
          <div><span className="lbl">Uptime</span><b>{cur.uptimeSeconds == null ? `unknown (${cur.status})` : fmtUptime(cur.uptimeSeconds)}</b></div>
          <div><span className="lbl">Last restart</span><b>{cur.uptimeSeconds == null ? '—' : new Date(Date.now() - cur.uptimeSeconds * 1000).toLocaleString()}</b></div>
          <div><span className="lbl">Read at</span><b>{ago(cur.sampledAt)}</b></div>
          <div><Link className="btn small" to={`/switches/${cur.switchId}`}>Open switch</Link></div>
        </div>
      )}
    </div>
  );
}
