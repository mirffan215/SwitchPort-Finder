import { useState, type ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api } from '../api';
import { BASE, RANGES, SwitchPicker, Viz, toQuery, type Def } from '../lib/reportShared';

type Cell = { title: string; note: string; def: Partial<Def>; span?: 1 | 2 | 3; tall?: boolean };
interface Report { key: string; name: string; blurb: string; cells: Cell[] }

const sum = (field: string, agg = 'avg') => ({ field, agg });
const REPORTS: Report[] = [
  { key: 'summary', name: 'Executive summary', blurb: 'One page for management: availability, capacity, devices and Wi-Fi.', cells: [
    { title: 'Availability by switch', note: 'Share of time each switch answered. Lowest first, so the weakest link is on top.', span: 2, def: { dataset: 'availability', mode: 'summary', chart: 'bar', groupBy: ['switch'], measures: [sum('availabilityPct', 'min')], sortBy: 'min_availabilityPct', desc: false, limit: 12 } },
    { title: 'Devices by type', note: 'What is connected to the network.', def: { dataset: 'devices', mode: 'summary', chart: 'donut', groupBy: ['category'], measures: [{ field: '*', agg: 'count' }], sortBy: 'count', desc: true } },
    { title: 'Network traffic', note: 'Average inbound and outbound across all ports.', span: 2, def: { dataset: 'port_traffic', mode: 'summary', chart: 'area', groupBy: ['at'], bucket: 'day', measures: [sum('inMbps'), sum('outMbps')] } },
    { title: 'Busiest links', note: 'Highest utilization seen on any port: where capacity will run out first.', def: { dataset: 'ports', mode: 'summary', chart: 'gauge', groupBy: ['switch', 'port'], measures: [sum('utilPct', 'max')], sortBy: 'max_utilPct', desc: true, limit: 6 } },
    { title: 'Wi-Fi clients per access point', note: 'From the wireless controller.', span: 2, def: { dataset: 'access_points', mode: 'summary', chart: 'bar', groupBy: ['name'], measures: [sum('wifiClients', 'max')], sortBy: 'max_wifiClients', desc: true, limit: 10 } },
    { title: 'Ports by type', note: 'How much of the port capacity is used for devices and uplinks.', def: { dataset: 'ports', mode: 'summary', chart: 'donut', groupBy: ['type'], measures: [{ field: '*', agg: 'count' }] } },
  ] },
  { key: 'performance', name: 'Network performance', blurb: 'Load on the switches and links, and where users may feel slowness.', cells: [
    { title: 'CPU and memory', note: 'Average load of all switches over the period.', span: 2, def: { dataset: 'switch_health', mode: 'summary', chart: 'area', groupBy: ['at'], bucket: 'day', measures: [sum('cpuPct'), sum('memPct')] } },
    { title: 'Busiest switches by CPU', note: 'Peak CPU per switch.', def: { dataset: 'availability', mode: 'summary', chart: 'bar', groupBy: ['switch'], measures: [sum('maxCpuPct', 'max')], sortBy: 'max_maxCpuPct', desc: true, limit: 8 } },
    { title: 'Uplink utilization', note: 'Switch-to-switch links, as a share of their speed.', span: 2, def: { dataset: 'port_traffic', mode: 'summary', chart: 'line', groupBy: ['at'], bucket: 'day', filters: [{ field: 'type', op: 'eq', value: 'SwitchUplink' }], measures: [sum('utilPct'), sum('utilPct', 'max')] } },
    { title: 'Response time by switch', note: 'Average and worst SNMP round-trip in milliseconds.', span: 1, def: { dataset: 'switch_health', mode: 'summary', chart: 'column', groupBy: ['switch'], measures: [sum('latencyMs'), sum('latencyMs', 'max')], sortBy: 'avg_latencyMs', desc: true, limit: 8 } },
    { title: 'Drops and errors', note: 'Worst packet drops and errors per second, by switch.', span: 3, def: { dataset: 'port_traffic', mode: 'summary', chart: 'column', groupBy: ['switch'], measures: [sum('discardsPerSec', 'max'), sum('errorsPerSec', 'max')], sortBy: 'max_discardsPerSec', desc: true, limit: 12 } },
  ] },
  { key: 'availability', name: 'Availability and reliability', blurb: 'Uptime, outages and restarts: is the network dependable?', cells: [
    { title: 'Availability', note: 'Lowest availability first.', span: 2, def: { dataset: 'availability', mode: 'summary', chart: 'bar', groupBy: ['switch'], measures: [sum('availabilityPct', 'min')], sortBy: 'min_availabilityPct', desc: false, limit: 15 } },
    { title: 'Switch uptime', note: 'Days since the last restart.', def: { dataset: 'switches', mode: 'summary', chart: 'gauge', groupBy: ['name'], measures: [sum('uptimeDays', 'max')], sortBy: 'max_uptimeDays', desc: false, limit: 6 } },
    { title: 'Downtime', note: 'Minutes unreachable per switch.', span: 2, def: { dataset: 'availability', mode: 'summary', chart: 'column', groupBy: ['switch'], measures: [sum('downtimeMinutes', 'sum')], sortBy: 'sum_downtimeMinutes', desc: true, limit: 12 } },
    { title: 'Outages and restarts', note: 'How often each switch failed or rebooted.', def: { dataset: 'availability', mode: 'summary', chart: 'bar', groupBy: ['switch'], measures: [sum('outages', 'sum')], sortBy: 'sum_outages', desc: true, limit: 8 } },
    { title: 'Availability detail', note: 'All figures for the period.', span: 3, tall: true, def: { dataset: 'availability', mode: 'table', chart: 'table', columns: ['switch', 'availabilityPct', 'downtimeMinutes', 'outages', 'restarts', 'avgCpuPct', 'avgLatencyMs'], sortBy: 'availabilityPct', desc: false, limit: 50 } },
  ] },
  { key: 'inventory', name: 'Inventory and growth', blurb: 'What is on the network, by type, maker, location and Wi-Fi.', cells: [
    { title: 'Devices by manufacturer', note: 'Top makers on the network.', def: { dataset: 'devices', mode: 'summary', chart: 'donut', groupBy: ['vendor'], measures: [{ field: '*', agg: 'count' }], sortBy: 'count', desc: true, limit: 8 } },
    { title: 'Devices per switch', note: 'Where devices connect.', span: 2, def: { dataset: 'devices', mode: 'summary', chart: 'bar', groupBy: ['switch'], measures: [{ field: '*', agg: 'count' }], sortBy: 'count', desc: true, limit: 12 } },
    { title: 'Devices by state', note: 'Online, offline or stale at the last scan.', def: { dataset: 'devices', mode: 'summary', chart: 'donut', groupBy: ['state'], measures: [{ field: '*', agg: 'count' }] } },
    { title: 'Access points per switch', note: 'Access points by the switch they plug into.', span: 2, def: { dataset: 'devices', mode: 'summary', chart: 'bar', groupBy: ['switch'], filters: [{ field: 'category', op: 'eq', value: 'AccessPoint' }], measures: [{ field: '*', agg: 'count' }], sortBy: 'count', desc: true, limit: 12 } },
    { title: 'Wi-Fi clients per access point', note: 'From the wireless controller.', span: 2, def: { dataset: 'access_points', mode: 'summary', chart: 'bar', groupBy: ['name'], measures: [sum('wifiClients', 'max')], sortBy: 'max_wifiClients', desc: true, limit: 12 } },
    { title: 'Ports by state', note: 'Connected, uplink and unused ports.', def: { dataset: 'ports', mode: 'summary', chart: 'donut', groupBy: ['type'], measures: [{ field: '*', agg: 'count' }] } },
  ] },
];

function Card({ cell, range, switchIds }: { cell: Cell; range: string; switchIds: number[] }) {
  const def: Def = { ...BASE, ...cell.def, range, switchIds, title: cell.title };
  const q = useQuery({ queryKey: ['exec-card', cell.title, range, switchIds.join(',')], queryFn: () => api.runReport(toQuery(def)), staleTime: 60_000 });
  return (
    <section className={`xcard span${cell.span ?? 1}`}>
      <header><h3>{cell.title}</h3><p>{cell.note}</p></header>
      <div className={`xviz ${cell.tall ? 'tall' : ''}`}>
        {q.isLoading ? <p className="muted">Loading…</p> : q.error ? <p className="muted">This chart could not be built.</p> : q.data ? <Viz res={q.data} def={def} onPick={() => undefined} onSort={() => undefined} /> : null}
      </div>
    </section>
  );
}

function Stat({ label, value, sub, tone }: { label: string; value: ReactNode; sub?: string; tone?: 'ok' | 'warn' | 'bad' }) {
  return <div className={`xstat ${tone ?? ''}`}><span>{label}</span><b>{value}</b>{sub && <small>{sub}</small>}</div>;
}

/** Headline numbers for the executive summary. */
function Headline({ range, switchIds }: { range: string; switchIds: number[] }) {
  const dash = useQuery({ queryKey: ['dashboard'], queryFn: api.dashboard });
  const aps = useQuery({ queryKey: ['access-points'], queryFn: api.accessPoints });
  const alerts = useQuery({ queryKey: ['alerts'], queryFn: api.alerts });
  const def: Def = { ...BASE, dataset: 'availability', mode: 'summary', chart: 'kpi', groupBy: [], measures: [sum('availabilityPct'), sum('downtimeMinutes', 'sum'), sum('outages', 'sum')], range, switchIds };
  const av = useQuery({ queryKey: ['exec-head', range, switchIds.join(',')], queryFn: () => api.runReport(toQuery(def)), staleTime: 60_000 });
  const row = av.data?.rows[0]; const pct = typeof row?.[0] === 'number' ? row[0] : null; const down = typeof row?.[1] === 'number' ? row[1] : null; const outages = typeof row?.[2] === 'number' ? row[2] : null;
  const d = dash.data;
  return (
    <div className="xstats">
      <Stat label="Network availability" value={pct == null ? '—' : `${pct.toFixed(2)}%`} sub={down == null ? 'history builds up as switches are polled' : `${Math.round(down)} min down · ${outages ?? 0} outage${outages === 1 ? '' : 's'}`} tone={pct == null ? undefined : pct >= 99.9 ? 'ok' : pct >= 99 ? 'warn' : 'bad'} />
      <Stat label="Switches" value={d?.switches ?? '—'} sub={d ? `${d.switchUplinks} uplinks` : undefined} />
      <Stat label="Devices online" value={d ? `${d.devices - d.offlineDevices}` : '—'} sub={d ? `of ${d.devices} · ${d.offlineDevices} offline` : undefined} tone={d && d.devices > 0 && d.offlineDevices / d.devices > 0.2 ? 'warn' : undefined} />
      <Stat label="Access points online" value={aps.data ? `${aps.data.summary.online}` : '—'} sub={aps.data ? `of ${aps.data.summary.total} · ${aps.data.summary.wifiClients ?? 0} Wi-Fi clients` : undefined} tone={aps.data && aps.data.summary.offline > 0 ? 'warn' : undefined} />
      <Stat label="Ports in use" value={d && d.totalPorts ? `${Math.round(((d.totalPorts - d.downPorts) / d.totalPorts) * 100)}%` : '—'} sub={d ? `${d.totalPorts - d.downPorts} of ${d.totalPorts} up` : undefined} />
      <Stat label="Open alerts" value={alerts.data?.activeCount ?? '—'} sub={alerts.data?.activeCount ? 'needs review' : 'none open'} tone={alerts.data?.activeCount ? 'bad' : 'ok'} />
    </div>
  );
}

/** Ready-made graphical reports for management, with a print / save-as-PDF button. */
export default function ExecutiveReports() {
  const [key, setKey] = useState('summary'); const [range, setRange] = useState('30d'); const [switchIds, setSwitchIds] = useState<number[]>([]);
  const switches = useQuery({ queryKey: ['switches'], queryFn: api.listSwitches });
  const rep = REPORTS.find((r) => r.key === key)!; const days = RANGES.find((r) => r[0] === range)?.[1] ?? '';
  const scope = switchIds.length === 0 ? 'all switches' : switchIds.length === 1 ? (switches.data?.find((s) => s.id === switchIds[0])?.name ?? '1 switch') : `${switchIds.length} switches`;
  return (
    <div className="xr">
      <div className="xr-controls">
        <div className="xr-tabs" role="tablist" aria-label="Report">
          {REPORTS.map((r) => <button key={r.key} role="tab" aria-selected={key === r.key} className={key === r.key ? 'on' : ''} onClick={() => setKey(r.key)}><b>{r.name}</b><span>{r.blurb}</span></button>)}
        </div>
        <div className="xr-tools">
          <div className="chips" role="group" aria-label="Period">{RANGES.filter((r) => r[0] !== 'custom' && r[0] !== '24h').map(([k, l]) => <button key={k} className="chipbtn" aria-pressed={range === k} onClick={() => setRange(k)}>{l}</button>)}</div>
          <div style={{ minWidth: 220 }}><SwitchPicker all={switches.data ?? []} value={switchIds} onChange={setSwitchIds} /></div>
          <button type="button" className="btn primary" onClick={() => window.print()}>Print or save as PDF</button>
        </div>
      </div>
      <article className="xr-sheet">
        <header className="xr-head">
          <div><h2>{rep.name}</h2><p>{rep.blurb}</p></div>
          <div className="xr-meta"><span>{days}</span><span>{scope}</span><span>Generated {new Date().toLocaleString()}</span></div>
        </header>
        {key === 'summary' && <Headline range={range} switchIds={switchIds} />}
        <div className="xgrid">{rep.cells.map((c) => <Card key={`${key}-${c.title}`} cell={c} range={range} switchIds={switchIds} />)}</div>
        <footer className="xr-foot">SwitchPort Finder · read-only data collected by SNMP · history covers the period shown, from the first scan or poll of each switch.</footer>
      </article>
    </div>
  );
}
