import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { api, type TopPort } from '../api';
import { ago } from './Bits';
import { Gauge, HBars, fmtBps, fmtRate } from './Charts';

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

export function TopUtilization() {
  const { data } = useQuery({ queryKey: ['top-util'], queryFn: () => api.topPorts('utilization', 5), refetchInterval: 30_000 });
  const [sel, setSel] = useState<number | null>(null);
  const items = data?.items ?? []; const cur = items.find((i) => i.portId === sel) ?? null;
  return (
    <div className="panelx">
      <h2>Top 5 port utilization</h2>
      <p className="hint">Busier direction as % of link speed, averaged between the last two scans. Click a gauge for details.</p>
      {items.length === 0 ? <div className="empty">{data && data.portsWithData > 0 ? 'No ports with a known link speed yet.' : WAIT}</div> : (
        <div className="gauges">
          {items.map((p) => <Gauge key={p.portId} pct={p.traffic.utilizationPct ?? 0} label={p.portName} sub={p.switchName} selected={sel === p.portId}
            onSelect={() => setSel(sel === p.portId ? null : p.portId)} title={`${p.switchName} ${p.portName}: in ${fmtBps(p.traffic.inBps)}, out ${fmtBps(p.traffic.outBps)}`} />)}
        </div>
      )}
      {cur && <Detail p={cur} />}
    </div>
  );
}

export function TopDrops() {
  const { data } = useQuery({ queryKey: ['top-drops'], queryFn: () => api.topPorts('drops', 5), refetchInterval: 30_000 });
  const [sel, setSel] = useState<string | undefined>();
  const items = data?.items ?? []; const cur = items.find((i) => String(i.portId) === sel);
  return (
    <div className="panelx">
      <h2>Top 5 packet-drop ports</h2>
      <p className="hint">Discarded packets per second (in + out), averaged between the last two scans. Click a bar for details.</p>
      {items.length === 0 ? <div className="empty">{data && data.portsWithData > 0 ? 'No dropped packets in the last interval.' : WAIT}</div> : (
        <HBars selected={sel} onSelect={(k) => setSel(k === sel ? undefined : k)}
          bars={items.map((p) => ({ key: String(p.portId), label: p.portName, sub: p.switchName, value: p.traffic.discardsPerSec, text: `${fmtRate(p.traffic.discardsPerSec)} /s`, detail: `${p.switchName} ${p.portName}` }))} />
      )}
      {cur && <Detail p={cur} />}
    </div>
  );
}
