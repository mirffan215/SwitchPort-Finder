import { useEffect, useMemo, useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient, keepPreviousData } from '@tanstack/react-query';
import { api, type SavedReport } from '../api';
import { ErrorBox } from '../components/Bits';
import { useAuth } from '../components/Auth';
import ExecutiveReports from './ExecutiveReports';
import { BASE, RANGES, SwitchPicker, Viz, toQuery, type Chart, type Def } from '../lib/reportShared';

const CHARTS: [Chart, string][] = [['table', 'Table'], ['column', 'Columns'], ['bar', 'Bars'], ['line', 'Line'], ['area', 'Area'], ['donut', 'Donut'], ['gauge', 'Gauges'], ['kpi', 'Numbers']];
const AGGS = [['avg', 'Average'], ['max', 'Maximum'], ['min', 'Minimum'], ['sum', 'Total'], ['count', 'Count'], ['distinct', 'Distinct count']];
const OPS = [['eq', '='], ['ne', '≠'], ['contains', 'contains'], ['gt', '>'], ['gte', '≥'], ['lt', '<'], ['lte', '≤'], ['in', 'in list']];

const PRESETS: { name: string; def: Partial<Def> }[] = [
  { name: 'Switch health overview', def: { dataset: 'switches', mode: 'table', chart: 'table', columns: ['name', 'status', 'uptimeDays', 'cpuPct', 'memPct', 'latencyMs', 'portsUp', 'ports'], sortBy: 'cpuPct', desc: true } },
  { name: 'Top 10 busiest ports', def: { dataset: 'ports', mode: 'summary', chart: 'bar', groupBy: ['switch', 'port'], measures: [{ field: 'utilPct', agg: 'max' }], sortBy: 'max_utilPct', desc: true, limit: 10 } },
  { name: 'Uplink utilization trend', def: { dataset: 'port_traffic', mode: 'summary', chart: 'line', groupBy: ['at'], bucket: 'hour', range: '7d', filters: [{ field: 'type', op: 'eq', value: 'SwitchUplink' }], measures: [{ field: 'utilPct', agg: 'avg' }, { field: 'utilPct', agg: 'max' }] } },
  { name: 'CPU and memory trend', def: { dataset: 'switch_health', mode: 'summary', chart: 'area', groupBy: ['at'], bucket: 'day', range: '30d', measures: [{ field: 'cpuPct', agg: 'avg' }, { field: 'memPct', agg: 'avg' }] } },
  { name: 'Availability (last 30 days)', def: { dataset: 'availability', mode: 'summary', chart: 'bar', range: '30d', groupBy: ['switch'], measures: [{ field: 'availabilityPct', agg: 'min' }], sortBy: 'min_availabilityPct', desc: false } },
  { name: 'Downtime by switch', def: { dataset: 'availability', mode: 'summary', chart: 'column', range: '30d', groupBy: ['switch'], measures: [{ field: 'downtimeMinutes', agg: 'sum' }, { field: 'outages', agg: 'sum' }], sortBy: 'sum_downtimeMinutes', desc: true } },
  { name: 'Latency by switch', def: { dataset: 'switch_health', mode: 'summary', chart: 'column', range: '7d', groupBy: ['switch'], measures: [{ field: 'latencyMs', agg: 'avg' }, { field: 'latencyMs', agg: 'max' }], sortBy: 'avg_latencyMs', desc: true } },
  { name: 'Packet drops and errors by switch', def: { dataset: 'port_traffic', mode: 'summary', chart: 'column', range: '7d', groupBy: ['switch'], measures: [{ field: 'discardsPerSec', agg: 'max' }, { field: 'errorsPerSec', agg: 'max' }], sortBy: 'max_discardsPerSec', desc: true } },
  { name: 'Access points by switch', def: { dataset: 'devices', mode: 'summary', chart: 'bar', groupBy: ['switch'], filters: [{ field: 'category', op: 'eq', value: 'AccessPoint' }], measures: [{ field: '*', agg: 'count' }], sortBy: 'count', desc: true } },
  { name: 'Devices by type', def: { dataset: 'devices', mode: 'summary', chart: 'donut', groupBy: ['category'], measures: [{ field: '*', agg: 'count' }], sortBy: 'count', desc: true } },
  { name: 'Devices by manufacturer', def: { dataset: 'devices', mode: 'summary', chart: 'donut', groupBy: ['vendor'], measures: [{ field: '*', agg: 'count' }], sortBy: 'count', desc: true, limit: 12 } },
  { name: 'Ports by type', def: { dataset: 'ports', mode: 'summary', chart: 'donut', groupBy: ['type'], measures: [{ field: '*', agg: 'count' }] } },
  { name: 'Monthly traffic trend', def: { dataset: 'port_traffic', mode: 'summary', chart: 'line', groupBy: ['at'], bucket: 'month', range: '90d', measures: [{ field: 'inMbps', agg: 'avg' }, { field: 'outMbps', agg: 'avg' }] } },
];

const useDebounced = <T,>(v: T, ms = 450) => { const [x, setX] = useState(v); useEffect(() => { const t = setTimeout(() => setX(v), ms); return () => clearTimeout(t); }, [v, ms]); return x; };
export default function ReportsPage() {
  const [view, setView] = useState<'exec' | 'build'>('exec');
  return (
    <section className="reports">
      <div className="toolbar"><h1>Reports</h1><div className="seg" role="group" aria-label="Report type"><button type="button" className={view === 'exec' ? 'on' : ''} aria-pressed={view === 'exec'} onClick={() => setView('exec')}>Management reports</button><button type="button" className={view === 'build' ? 'on' : ''} aria-pressed={view === 'build'} onClick={() => setView('build')}>Report builder</button></div></div>
      {view === 'exec' ? <ExecutiveReports /> : <ReportBuilder />}
    </section>
  );
}

function ReportBuilder() {
  const { canWrite } = useAuth(); const qc = useQueryClient();
  const [def, setDef] = useState<Def>({ ...BASE, ...PRESETS[0].def });
  const [savedId, setSavedId] = useState<number | null>(null); const [msg, setMsg] = useState<string | null>(null);
  const canvasRef = useRef<HTMLDivElement>(null);
  const set = (p: Partial<Def>) => setDef((d) => ({ ...d, ...p }));
  const datasets = useQuery({ queryKey: ['report-datasets'], queryFn: api.reportDatasets, staleTime: Infinity });
  const saved = useQuery({ queryKey: ['reports'], queryFn: api.savedReports });
  const switches = useQuery({ queryKey: ['switches'], queryFn: api.listSwitches });
  const ds = datasets.data?.find((x) => x.key === def.dataset); const fields = ds?.fields ?? [];
  const nums = fields.filter((f) => f.type === 'Number'); const dims = fields.filter((f) => f.type !== 'Number' || !f.measure);
  const qDef = useDebounced(def);
  const res = useQuery({ queryKey: ['report-run', JSON.stringify({ ...qDef, title: '' })], queryFn: () => api.runReport(toQuery(qDef)), placeholderData: keepPreviousData, enabled: !!ds && (qDef.mode === 'summary' || qDef.columns.length > 0) });

  const choose = (name: string, d: Partial<Def>) => { setDef({ ...BASE, ...d, title: name, switchIds: def.switchIds ?? [] }); setSavedId(null); setMsg(null); };
  const changeDataset = (key: string) => { const f = datasets.data?.find((x) => x.key === key); set({ dataset: key, columns: f?.fields.slice(0, 6).map((x) => x.key) ?? [], groupBy: [], measures: [], filters: [], sortBy: undefined, range: f?.history ? '7d' : def.range }); };
  const addMeasure = () => set({ measures: [...def.measures, { field: nums[0]?.key ?? '*', agg: nums.length ? 'avg' : 'count' }] });
  const pick = (cat: string) => { if (def.mode === 'summary' && def.groupBy.length === 1 && def.groupBy[0] !== 'at') set({ filters: [...def.filters.filter((f) => !(f.field === def.groupBy[0] && f.op === 'eq')), { field: def.groupBy[0], op: 'eq', value: cat }] }); };

  const save = useMutation({
    mutationFn: async (asNew: boolean) => { const body = JSON.stringify(def); return savedId && !asNew ? api.updateReport(savedId, def.title, body) : api.saveReport(def.title, body); },
    onSuccess: (r) => { setSavedId(r.id); setMsg('Saved.'); qc.invalidateQueries({ queryKey: ['reports'] }); },
  });
  const del = useMutation({ mutationFn: () => api.deleteReport(savedId!), onSuccess: () => { setSavedId(null); setMsg('Deleted.'); qc.invalidateQueries({ queryKey: ['reports'] }); } });
  const load = (r: SavedReport) => { try { setDef({ ...BASE, ...JSON.parse(r.json), title: r.name }); setSavedId(r.id); setMsg(null); } catch { setMsg('That saved report could not be read.'); } };
  const download = async (format: 'xlsx' | 'csv') => { const { blob, name } = await api.exportReport(toQuery(def), def.title, format); const a = document.createElement('a'); a.href = URL.createObjectURL(blob); a.download = name; a.click(); URL.revokeObjectURL(a.href); };
  const exp = useMutation({ mutationFn: download });
  const pdf = useMutation({ mutationFn: async () => { if (!res.data) return; const { exportReportPdf } = await import('../lib/reportPdf'); await exportReportPdf(def.chart === 'table' ? null : canvasRef.current?.querySelector('.viz') as HTMLElement | null, def.title, subtitle(def, ds?.label), res.data); } });
  const sortable = useMemo(() => res.data?.columns ?? [], [res.data]);

  const toggleCol = (k: string) => set({ columns: def.columns.includes(k) ? def.columns.filter((c) => c !== k) : [...def.columns, k] });
  return (
    <div className="builder">
      <div className="toolbar"><h2 style={{ margin: 0, fontSize: 18 }}>Report builder</h2><div className="spacer" />
        <button className="btn" disabled={!res.data || exp.isPending} onClick={() => exp.mutate('xlsx')}>Export Excel</button>
        <button className="btn" disabled={!res.data || exp.isPending} onClick={() => exp.mutate('csv')}>Export CSV</button>
        <button className="btn primary" disabled={!res.data || pdf.isPending} onClick={() => pdf.mutate()}>{pdf.isPending ? 'Building PDF…' : 'Export PDF'}</button>
      </div>
      <ErrorBox error={res.error ?? exp.error ?? pdf.error ?? save.error ?? del.error} />
      <div className="rb">
        <aside className="rb-side" aria-label="Report builder">
          <label className="field"><span>Report name</span><input value={def.title} onChange={(e) => set({ title: e.target.value })} maxLength={150} /></label>
          <div><h3>Start from</h3>
            <div className="presets">{PRESETS.map((p) => <button key={p.name} className="btn small" style={{ marginLeft: 0 }} onClick={() => choose(p.name, p.def)}>{p.name}</button>)}</div>
            {(saved.data?.length ?? 0) > 0 && <label className="field" style={{ marginTop: 8 }}><span>Saved reports</span><select value={savedId ?? ''} onChange={(e) => { const r = saved.data?.find((x) => x.id === Number(e.target.value)); if (r) load(r); }}><option value="">— choose —</option>{saved.data?.map((r) => <option key={r.id} value={r.id}>{r.name}</option>)}</select></label>}
          </div>
          <div><h3>Switches</h3>
            <SwitchPicker all={switches.data ?? []} value={def.switchIds ?? []} onChange={(v) => set({ switchIds: v })} />
          </div>
          <div><h3>Data</h3>
            <select value={def.dataset} onChange={(e) => changeDataset(e.target.value)} aria-label="Dataset">{datasets.data?.map((d) => <option key={d.key} value={d.key}>{d.label}</option>)}</select>
            <p className="muted small" style={{ margin: '4px 0 0' }}>{ds?.description}</p>
          </div>
          {ds?.history && <div><h3>Period</h3>
            <div className="chips">{RANGES.map(([k, l]) => <button key={k} className="chipbtn" aria-pressed={def.range === k} onClick={() => set({ range: k })}>{l}</button>)}</div>
            {def.range === 'custom' && <div className="rb-row" style={{ gridTemplateColumns: '1fr 1fr', marginTop: 6 }}><input type="date" aria-label="From" value={def.from ?? ''} onChange={(e) => set({ from: e.target.value })} /><input type="date" aria-label="To" value={def.to ?? ''} onChange={(e) => set({ to: e.target.value })} /></div>}
          </div>}
          <div><h3>Show as</h3>
            <div className="chips">{CHARTS.map(([k, l]) => <button key={k} className="chipbtn" aria-pressed={def.chart === k} onClick={() => set({ chart: k, mode: k === 'table' && def.mode === 'table' ? 'table' : def.mode === 'table' && k !== 'table' ? 'summary' : def.mode, ...(k !== 'table' && def.mode === 'table' && !def.measures.length ? { measures: nums.length ? [{ field: nums[0].key, agg: 'avg' }] : [{ field: '*', agg: 'count' }] } : {}) })}>{l}</button>)}</div>
            <div className="chips" style={{ marginTop: 8 }}>{(['table', 'summary'] as const).map((m) => <button key={m} className="chipbtn" aria-pressed={def.mode === m} onClick={() => set({ mode: m, chart: m === 'table' ? 'table' : def.chart === 'table' ? 'column' : def.chart, measures: m === 'summary' && !def.measures.length ? [{ field: '*', agg: 'count' }] : def.measures })}>{m === 'table' ? 'List of rows' : 'Group and summarise'}</button>)}</div>
          </div>
          {def.mode === 'table' ? (
            <div><h3>Columns</h3><div className="chips">{fields.map((f) => <button key={f.key} className="chipbtn" aria-pressed={def.columns.includes(f.key)} onClick={() => toggleCol(f.key)}>{f.label}</button>)}</div></div>
          ) : (
            <>
              <div><h3>Group by (axis)</h3>
                <div className="chips">{dims.map((f) => <button key={f.key} className="chipbtn" aria-pressed={def.groupBy.includes(f.key)} onClick={() => set({ groupBy: def.groupBy.includes(f.key) ? def.groupBy.filter((g) => g !== f.key) : [...def.groupBy, f.key].slice(-2) })}>{f.label}</button>)}</div>
                {def.groupBy.includes('at') && <label className="field" style={{ marginTop: 6 }}><span>Time step</span><select value={def.bucket} onChange={(e) => set({ bucket: e.target.value })}><option value="hour">Hour</option><option value="day">Day</option><option value="week">Week</option><option value="month">Month</option></select></label>}
              </div>
              <div><h3>Values</h3>
                {def.measures.map((m, i) => (
                  <div className="rb-row" key={i} style={{ marginBottom: 6 }}>
                    <select aria-label="Aggregation" value={m.agg} onChange={(e) => set({ measures: def.measures.map((x, j) => (j === i ? { ...x, agg: e.target.value } : x)) })}>{AGGS.map(([k, l]) => <option key={k} value={k}>{l}</option>)}</select>
                    <select aria-label="Field" value={m.field} onChange={(e) => set({ measures: def.measures.map((x, j) => (j === i ? { ...x, field: e.target.value } : x)) })}><option value="*">Rows</option>{fields.map((f) => <option key={f.key} value={f.key}>{f.label}{f.unit ? ` (${f.unit})` : ''}</option>)}</select>
                    <button className="link" aria-label="Remove value" onClick={() => set({ measures: def.measures.filter((_, j) => j !== i) })}>✕</button>
                  </div>))}
                <button className="btn small" style={{ marginLeft: 0 }} onClick={addMeasure}>+ Add value</button>
              </div>
            </>
          )}
          <div><h3>Filters</h3>
            {def.filters.map((f, i) => (
              <div className="rb-row f" key={i} style={{ marginBottom: 6 }}>
                <select aria-label="Filter field" value={f.field} onChange={(e) => set({ filters: def.filters.map((x, j) => (j === i ? { ...x, field: e.target.value } : x)) })}>{fields.map((x) => <option key={x.key} value={x.key}>{x.label}</option>)}</select>
                <select aria-label="Operator" value={f.op} onChange={(e) => set({ filters: def.filters.map((x, j) => (j === i ? { ...x, op: e.target.value } : x)) })}>{OPS.map(([k, l]) => <option key={k} value={k}>{l}</option>)}</select>
                <input aria-label="Value" value={f.value ?? ''} onChange={(e) => set({ filters: def.filters.map((x, j) => (j === i ? { ...x, value: e.target.value } : x)) })} />
                <button className="link" aria-label="Remove filter" onClick={() => set({ filters: def.filters.filter((_, j) => j !== i) })}>✕</button>
              </div>))}
            <button className="btn small" style={{ marginLeft: 0 }} onClick={() => set({ filters: [...def.filters, { field: fields[0]?.key ?? '', op: 'contains', value: '' }] })}>+ Add filter</button>
          </div>
          <div className="rb-row" style={{ gridTemplateColumns: '1fr 1fr' }}>
            <label className="field"><span>Sort by</span><select value={def.sortBy ?? ''} onChange={(e) => set({ sortBy: e.target.value || undefined })}><option value="">Automatic</option>{sortable.map((c) => <option key={c.key} value={c.key}>{c.label}</option>)}</select></label>
            <label className="field"><span>Show at most</span><select value={def.limit} onChange={(e) => set({ limit: Number(e.target.value) })}>{[5, 10, 20, 50, 100, 500, 1000, 5000].map((n) => <option key={n} value={n}>{n} rows</option>)}</select></label>
          </div>
          <label className="check"><input type="checkbox" checked={def.desc} onChange={(e) => set({ desc: e.target.checked })} /> Highest first</label>
          {canWrite && <div className="buttons" style={{ flexWrap: 'wrap' }}>
            <button className="btn primary" disabled={save.isPending || !def.title.trim()} onClick={() => save.mutate(false)}>{savedId ? 'Save changes' : 'Save report'}</button>
            {savedId && <button className="btn" disabled={save.isPending} onClick={() => save.mutate(true)}>Save as new</button>}
            {savedId && <button className="btn" disabled={del.isPending} onClick={() => confirm('Delete this saved report?') && del.mutate()}>Delete</button>}
          </div>}
          {msg && <div className="muted small" role="status">{msg}</div>}
        </aside>
        <div className="rb-main" ref={canvasRef}>
          {def.filters.some((f) => f.field && (f.value ?? '') !== '') && <div className="fchips">{def.filters.filter((f) => f.value).map((f, i) => <button key={i} className="chipbtn" onClick={() => set({ filters: def.filters.filter((x) => x !== f) })} title="Remove this filter">{fields.find((x) => x.key === f.field)?.label ?? f.field} {OPS.find((o) => o[0] === f.op)?.[1]} {f.value}</button>)}</div>}
          <div className="rb-canvas">
            <h2>{def.title || 'Report'}</h2>
            <div className="sub">{subtitle(def, ds?.label)}{res.data ? ` · ${res.data.totalRows} row(s)${res.data.truncated ? ' (showing the first ' + res.data.rows.length + ')' : ''}` : ''}</div>
            <div className="viz">{res.isLoading ? <p className="muted">Loading…</p> : res.data ? <Viz res={res.data} def={def} onPick={pick} onSort={(k) => set(def.sortBy === k ? { desc: !def.desc } : { sortBy: k, desc: true })} /> : def.mode === 'table' && def.columns.length === 0 ? <p className="muted">Choose at least one column.</p> : null}</div>
          </div>
        </div>
      </div>
    </div>
  );
}

function subtitle(d: Def, label?: string) {
  const days = RANGES.find((r) => r[0] === d.range); const hist = ['switch_health', 'port_traffic', 'availability'].includes(d.dataset);
  const sw = d.switchIds?.length ? ` · ${d.switchIds.length} switch${d.switchIds.length === 1 ? '' : 'es'}` : ' · all switches';
  return `${label ?? d.dataset}${sw}${hist ? ` · ${d.range === 'custom' ? `${d.from ?? '…'} to ${d.to ?? 'now'}` : days?.[1]}` : ' · current state'}`;
}
