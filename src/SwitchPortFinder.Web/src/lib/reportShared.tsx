import { useState } from 'react';
import { Donut, Gauge, XYChart, PALETTE } from '../components/Charts';
import type { ReportFilter, ReportMeasure, ReportQuery, ReportResult } from '../api';

export type Chart = 'table' | 'column' | 'bar' | 'line' | 'area' | 'donut' | 'gauge' | 'kpi';
export interface Def { switchIds: number[]; title: string; dataset: string; mode: 'summary' | 'table'; chart: Chart; columns: string[]; groupBy: string[]; measures: ReportMeasure[]; filters: ReportFilter[]; range: string; from?: string; to?: string; bucket: string; sortBy?: string; desc: boolean; limit: number }
export const BASE: Def = { switchIds: [], title: 'New report', dataset: 'switches', mode: 'table', chart: 'table', columns: [], groupBy: [], measures: [], filters: [], range: '7d', bucket: 'day', desc: true, limit: 50 };
export const RANGES: [string, string, number][] = [['24h', 'Last 24 hours', 1], ['7d', 'Last 7 days', 7], ['30d', 'Last 30 days', 30], ['90d', 'Last 90 days', 90], ['custom', 'Custom', 0]];
export function toQuery(d: Def): ReportQuery {
  const days = RANGES.find((r) => r[0] === d.range)?.[2] ?? 7; const now = new Date();
  const q: ReportQuery = { dataset: d.dataset, switchIds: d.switchIds?.length ? d.switchIds : undefined, filters: d.filters.filter((f) => f.field && (f.value ?? '') !== ''), sortBy: d.sortBy || undefined, desc: d.desc, limit: d.limit };
  if (d.range === 'custom') { if (d.from) q.from = new Date(d.from).toISOString(); if (d.to) q.to = new Date(new Date(d.to).getTime() + 86_399_000).toISOString(); }
  else { q.from = new Date(now.getTime() - days * 86_400_000).toISOString(); q.to = now.toISOString(); }
  if (d.mode === 'table') q.columns = d.columns; else { q.groupBy = d.groupBy; q.measures = d.measures.length ? d.measures : [{ field: '*', agg: 'count' }]; q.timeBucket = d.bucket; }
  return q;
}
export const fmt = (v: string | number | null, unit?: string | null) => (v == null ? '—' : typeof v === 'number' ? `${v.toLocaleString(undefined, { maximumFractionDigits: 2 })}${unit ? ' ' + unit : ''}` : v);

export function Viz({ res, def, onPick, onSort }: { res: ReportResult; def: Def; onPick: (c: string) => void; onSort: (k: string) => void }) {
  const gcount = def.mode === 'summary' ? def.groupBy.length : 0;
  const catIdx = def.mode === 'summary' ? res.columns.slice(0, Math.max(1, gcount)).map((_, i) => i) : [Math.max(0, res.columns.findIndex((c) => c.type === 'Text'))];
  const cats = res.rows.map((r) => catIdx.map((i) => (r[i] == null ? '—' : String(r[i]))).join(' · '));
  const numCols = res.columns.map((c, i) => ({ c, i })).filter(({ c, i }) => c.type === 'Number' && (def.mode === 'table' || i >= gcount));
  if (res.rows.length === 0) return <p className="muted">No data for this selection yet. History fills in as switches are scanned and polled.</p>;

  if (def.chart === 'table' || numCols.length === 0) return (
    <div className="tablewrap tall"><table>
      <thead><tr>{res.columns.map((c) => <th key={c.key} className="sortable" onClick={() => onSort(c.key)}>{c.label}{c.unit ? ` (${c.unit})` : ''}{def.sortBy === c.key ? (def.desc ? ' ▼' : ' ▲') : ''}</th>)}</tr></thead>
      <tbody>{res.rows.map((r, i) => <tr key={i}>{r.map((v, j) => <td key={j} className={typeof v === 'number' ? 'mono' : ''}>{fmt(v)}</td>)}</tr>)}</tbody>
    </table></div>
  );
  if (def.chart === 'kpi') return (
    <div className="kpis">{numCols.map(({ c, i }) => {
      const vals = res.rows.map((r) => r[i]).filter((v): v is number => typeof v === 'number'); const agg = def.measures[i - gcount]?.agg ?? 'avg';
      const v = vals.length === 0 ? null : res.rows.length === 1 ? vals[0] : agg === 'sum' || agg === 'count' ? vals.reduce((a, b) => a + b, 0) : agg === 'max' ? Math.max(...vals) : agg === 'min' ? Math.min(...vals) : vals.reduce((a, b) => a + b, 0) / vals.length;
      return <div className="kpi" key={c.key}><b>{fmt(v == null ? null : Number(v.toFixed(2)), c.unit)}</b><span>{c.label}</span></div>;
    })}</div>
  );
  const { c: c0, i: i0 } = numCols[0];
  if (def.chart === 'donut') {
    const top = res.rows.map((r, k) => ({ label: cats[k], value: Number(r[i0] ?? 0) })).filter((x) => x.value > 0);
    const shown = top.slice(0, 9); const rest = top.slice(9).reduce((a, b) => a + b.value, 0); if (rest > 0) shown.push({ label: 'Other', value: rest });
    return <Donut centre={c0.label} caption={c0.label} slices={shown.map((s, k) => ({ ...s, color: PALETTE[k % PALETTE.length] === 'var(--accent)' || k < 6 ? PALETTE[k % 6] : `hsl(${(k * 47) % 360} 55% 55%)`, onClick: () => onPick(s.label) }))} />;
  }
  if (def.chart === 'gauge') {
    const max = c0.unit === '%' ? 100 : Math.max(1, ...res.rows.map((r) => Number(r[i0] ?? 0)));
    return <div className="gauges">{res.rows.slice(0, 12).map((r, k) => <Gauge key={cats[k] + k} pct={(Number(r[i0] ?? 0) / max) * 100} text={fmt(typeof r[i0] === 'number' ? Number((r[i0] as number).toFixed(1)) : null)} label={cats[k]} sub={c0.label} onSelect={() => onPick(cats[k])} tone={c0.unit === '%' ? undefined : 'ok'} />)}</div>;
  }
  const series = numCols.map(({ c, i }) => ({ name: c.label, unit: c.unit ?? undefined, values: res.rows.map((r) => (typeof r[i] === 'number' ? (r[i] as number) : null)) }));
  if (def.chart === 'bar') {   // horizontal bars: sorted list with value labels
    const max = Math.max(1e-9, ...res.rows.map((r) => Math.abs(Number(r[i0] ?? 0))));
    return <ul className="hbars">{res.rows.map((r, k) => <li key={k}><button type="button" onClick={() => onPick(cats[k])}><span className="hb-name" title={cats[k]}>{cats[k]}</span><span className="hb-track"><i style={{ width: `${Math.max(2, (Math.abs(Number(r[i0] ?? 0)) / max) * 100)}%`, background: 'var(--accent)' }} /></span><span className="hb-val">{fmt(typeof r[i0] === 'number' ? r[i0] : null, c0.unit)}</span></button></li>)}</ul>;
  }
  return <XYChart kind={def.chart as 'column' | 'line' | 'area'} cats={cats} series={series} unit={series.length === 1 ? c0.unit ?? undefined : undefined} onPick={onPick} />;
}

/** Pick one, several or all switches (empty = all). */
export function SwitchPicker({ all, value, onChange }: { all: { id: number; name: string }[]; value: number[]; onChange: (v: number[]) => void }) {
  const [open, setOpen] = useState(false); const [f, setF] = useState('');
  const shown = all.filter((s) => s.name.toLowerCase().includes(f.toLowerCase()));
  const toggle = (id: number) => onChange(value.includes(id) ? value.filter((x) => x !== id) : [...value, id]);
  const label = value.length === 0 ? 'All switches' : value.length === 1 ? (all.find((s) => s.id === value[0])?.name ?? '1 switch') : `${value.length} switches`;
  return (
    <div>
      <button type="button" className="btn" style={{ width: '100%', textAlign: 'left' }} aria-expanded={open} onClick={() => setOpen(!open)}>{label} <span className="muted" style={{ float: 'right' }}>{open ? '▲' : '▼'}</span></button>
      {open && (
        <div style={{ border: '1px solid var(--line)', borderRadius: 10, padding: 8, marginTop: 6, background: 'var(--panel2)' }}>
          <input placeholder="Find a switch" aria-label="Find a switch" value={f} onChange={(e) => setF(e.target.value)} style={{ width: '100%', marginBottom: 6 }} />
          <div className="buttons" style={{ marginBottom: 6 }}><button type="button" className="btn small" style={{ marginLeft: 0 }} onClick={() => onChange(all.map((s) => s.id))}>Select all</button><button type="button" className="btn small" onClick={() => onChange([])}>Clear (all switches)</button></div>
          <div style={{ maxHeight: 200, overflow: 'auto', display: 'grid', gap: 2 }}>
            {shown.map((s) => <label key={s.id} className="check"><input type="checkbox" checked={value.includes(s.id)} onChange={() => toggle(s.id)} /> {s.name}</label>)}
            {shown.length === 0 && <span className="muted small">No switch matches.</span>}
          </div>
        </div>
      )}
      {value.length > 0 && <div className="chips" style={{ marginTop: 6 }}>{value.slice(0, 6).map((id) => <button key={id} type="button" className="chipbtn" onClick={() => toggle(id)} title="Remove">{all.find((s) => s.id === id)?.name ?? id} ✕</button>)}{value.length > 6 && <span className="muted small">+{value.length - 6} more</span>}</div>}
    </div>
  );
}
