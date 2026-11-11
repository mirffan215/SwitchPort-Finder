import { useState, type ReactNode } from 'react';

/** Bits per second -> "123.4 Mbps". */
export function fmtBps(b?: number | null): string {
  if (b == null) return '—';
  const u = ['bps', 'Kbps', 'Mbps', 'Gbps', 'Tbps']; let i = 0; let v = b;
  while (v >= 1000 && i < u.length - 1) { v /= 1000; i++; }
  return `${v >= 100 || i === 0 ? v.toFixed(0) : v.toFixed(1)} ${u[i]}`;
}
/** Seconds -> "42d 3h", "5h 12m", "8m". */
export function fmtUptime(sec?: number | null): string {
  if (sec == null) return '—';
  const d = Math.floor(sec / 86400), h = Math.floor((sec % 86400) / 3600), m = Math.floor((sec % 3600) / 60);
  return d > 0 ? `${d}d ${h}h` : h > 0 ? `${h}h ${m}m` : `${m}m`;
}
export const fmtRate = (v: number) => (v >= 10 ? v.toFixed(0) : v >= 1 ? v.toFixed(1) : v.toFixed(2));
const level = (pct: number) => (pct >= 85 ? 'bad' : pct >= 60 ? 'warn' : 'ok');

/** Semi-circular gauge. Interactive: focusable, hover/selected highlight, click handler. */
export function Gauge({ pct, label, sub, selected, onSelect, title, text, tone }: { pct: number; label: string; sub?: string; selected?: boolean; onSelect?: () => void; title?: string; text?: string; tone?: 'ok' | 'warn' | 'bad' }) {
  const v = Math.max(0, Math.min(100, pct));
  return (
    <button type="button" className={`gauge ${selected ? 'sel' : ''}`} onClick={onSelect} title={title} aria-pressed={selected} aria-label={`${label}: ${text ?? `${v.toFixed(0)} percent`}`}>
      <svg viewBox="0 0 120 72" role="img" aria-hidden="true">
        <path d="M 10 62 A 50 50 0 0 1 110 62" pathLength={100} className="g-track" />
        <path d="M 10 62 A 50 50 0 0 1 110 62" pathLength={100} className={`g-val ${tone ?? level(v)}`} strokeDasharray={`${v} 100`} />
        <text x="60" y="56" textAnchor="middle" className={`g-num ${text ? 'sm' : ''}`}>{text ?? `${v < 10 && v > 0 ? v.toFixed(1) : v.toFixed(0)}%`}</text>
      </svg>
      <span className="g-label">{label}</span>
      {sub && <span className="g-sub">{sub}</span>}
    </button>
  );
}

export interface Slice { label: string; value: number; color: string; onClick?: () => void }

/** Donut with a centre total; hovering or focusing a legend row highlights its slice. */
export function Donut({ slices, centre, caption }: { slices: Slice[]; centre: ReactNode; caption?: string }) {
  const [hot, setHot] = useState<number | null>(null);
  const total = slices.reduce((a, s) => a + s.value, 0);
  let acc = 0;
  return (
    <div className="donut">
      <svg viewBox="0 0 120 120" role="img" aria-label={caption}>
        <circle cx="60" cy="60" r="46" className="d-track" />
        {total > 0 && slices.map((s, i) => {
          const frac = (s.value / total) * 100; const off = acc; acc += frac;
          if (frac <= 0) return null;
          return <circle key={s.label} cx="60" cy="60" r="46" pathLength={100} stroke={s.color} strokeDasharray={`${Math.max(frac - 0.6, 0.1)} ${100 - Math.max(frac - 0.6, 0.1)}`} strokeDashoffset={-off}
            className={`d-seg ${hot === i ? 'hot' : hot !== null ? 'dim' : ''}`} transform="rotate(-90 60 60)" onMouseEnter={() => setHot(i)} onMouseLeave={() => setHot(null)} onClick={s.onClick} />;
        })}
        <text x="60" y="58" textAnchor="middle" className="d-num">{hot !== null ? slices[hot].value : total}</text>
        <text x="60" y="74" textAnchor="middle" className="d-cap">{hot !== null ? slices[hot].label : (centre as string)}</text>
      </svg>
      <ul className="d-legend">
        {slices.map((s, i) => (
          <li key={s.label}><button type="button" onMouseEnter={() => setHot(i)} onMouseLeave={() => setHot(null)} onFocus={() => setHot(i)} onBlur={() => setHot(null)} onClick={s.onClick}>
            <i style={{ background: s.color }} />{s.label} <b>{s.value}</b>{total > 0 && <span className="muted"> {Math.round((s.value / total) * 100)}%</span>}</button></li>
        ))}
      </ul>
    </div>
  );
}

export interface Bar { key: string; label: string; sub?: string; value: number; text: string; detail?: string }

/** Horizontal bar chart with hover detail and click selection. */
export function HBars({ bars, selected, onSelect }: { bars: Bar[]; selected?: string; onSelect?: (key: string) => void }) {
  const max = Math.max(...bars.map((b) => b.value), 0.0001);
  return (
    <ul className="hbars">
      {bars.map((b) => (
        <li key={b.key}>
          <button type="button" className={selected === b.key ? 'sel' : ''} onClick={() => onSelect?.(b.key)} title={b.detail}>
            <span className="hb-name">{b.label}{b.sub && <span className="muted"> · {b.sub}</span>}</span>
            <span className="hb-track"><i style={{ width: `${Math.max(2, (b.value / max) * 100)}%` }} /></span>
            <span className="hb-val">{b.text}</span>
          </button>
        </li>
      ))}
    </ul>
  );
}

export const PALETTE = ['var(--accent)', 'var(--purple)', 'var(--ok)', 'var(--warn)', 'var(--bad)', '#60a5fa'];

export interface Series { name: string; values: (number | null)[]; unit?: string }

const niceMax = (v: number) => { if (v <= 0) return 1; const p = 10 ** Math.floor(Math.log10(v)); const n = v / p; return (n <= 1 ? 1 : n <= 2 ? 2 : n <= 5 ? 5 : 10) * p; };
const fmtNum = (v: number) => (Math.abs(v) >= 1000 ? `${(v / 1000).toFixed(v >= 10000 ? 0 : 1)}k` : v >= 100 ? v.toFixed(0) : v.toFixed(v === Math.round(v) ? 0 : 1));

/** Column, line or area chart over categories (or time). Hover shows exact values; clicking a category reports it (cross-filter). */
export function XYChart({ kind, cats, series, unit, onPick, selected }: { kind: 'column' | 'line' | 'area'; cats: string[]; series: Series[]; unit?: string; onPick?: (cat: string) => void; selected?: string | null }) {
  const [hover, setHover] = useState<number | null>(null);
  const W = 720, H = 300, L = 48, R = 12, T = 14, B = cats.length > 12 ? 70 : 46;
  const max = niceMax(Math.max(0, ...series.flatMap((s) => s.values.map((v) => v ?? 0))));
  const iw = W - L - R, ih = H - T - B; const n = Math.max(1, cats.length);
  const x = (i: number) => L + (kind === 'column' ? (i + 0.5) * (iw / n) : n === 1 ? iw / 2 : (i / (n - 1)) * iw);
  const y = (v: number) => T + ih - (v / max) * ih;
  const step = Math.max(1, Math.ceil(n / 14)); const bw = (iw / n) * 0.7 / series.length;
  return (
    <div className="xychart">
      <svg viewBox={`0 0 ${W} ${H}`} role="img" aria-label={`${kind} chart`}>
        {[0, 0.25, 0.5, 0.75, 1].map((f) => <g key={f}><line x1={L} x2={W - R} y1={y(max * f)} y2={y(max * f)} className="gridline" /><text x={L - 6} y={y(max * f) + 4} textAnchor="end" className="axis">{fmtNum(max * f)}</text></g>)}
        {unit && <text x={L} y={10} className="axis">{unit}</text>}
        {cats.map((c, i) => i % step === 0 && <text key={c + i} x={x(i)} y={H - B + 14} textAnchor={cats.length > 12 ? 'end' : 'middle'} className="axis" transform={cats.length > 12 ? `rotate(-35 ${x(i)} ${H - B + 14})` : undefined}>{c.length > 14 ? c.slice(0, 13) + '…' : c}</text>)}
        {kind !== 'column' && series.map((s, si) => {
          const pts = s.values.map((v, i) => (v == null ? null : [x(i), y(v)] as const));
          const d = pts.filter(Boolean).map((p, i) => `${i ? 'L' : 'M'}${p![0].toFixed(1)} ${p![1].toFixed(1)}`).join(' ');
          const first = pts.find(Boolean), last = [...pts].reverse().find(Boolean);
          return <g key={s.name}>{kind === 'area' && first && last && <path d={`${d} L${last[0]} ${y(0)} L${first[0]} ${y(0)} Z`} fill={PALETTE[si % 6]} opacity={0.18} />}<path d={d} fill="none" stroke={PALETTE[si % 6]} strokeWidth={2.2} strokeLinejoin="round" />
            {n <= 40 && pts.map((p, i) => p && <circle key={i} cx={p[0]} cy={p[1]} r={hover === i ? 5 : 2.6} fill={PALETTE[si % 6]} />)}</g>;
        })}
        {kind === 'column' && cats.map((c, i) => series.map((s, si) => { const v = s.values[i]; if (v == null) return null; return <rect key={`${c}-${si}`} x={x(i) - (bw * series.length) / 2 + si * bw} y={y(v)} width={Math.max(2, bw - 1)} height={Math.max(0, y(0) - y(v))} rx={2} fill={PALETTE[si % 6]} opacity={selected && selected !== c ? 0.35 : hover === null || hover === i ? 1 : 0.6} />; }))}
        {cats.map((c, i) => <rect key={'h' + i} x={x(i) - iw / n / 2} y={T} width={iw / n} height={ih} fill="transparent" style={{ cursor: onPick ? 'pointer' : 'default' }} onMouseEnter={() => setHover(i)} onMouseLeave={() => setHover(null)} onClick={() => onPick?.(c)} />)}
        {hover !== null && <line x1={x(hover)} x2={x(hover)} y1={T} y2={T + ih} className="cursorline" />}
      </svg>
      {hover !== null && (
        <div className="tip" style={{ left: `${(x(hover) / W) * 100}%` }}>
          <b>{cats[hover]}</b>{series.map((s, si) => <div key={s.name}><i style={{ background: PALETTE[si % 6] }} />{s.name}: <b>{s.values[hover] == null ? '—' : `${s.values[hover]!.toLocaleString(undefined, { maximumFractionDigits: 2 })}${s.unit ? ' ' + s.unit : ''}`}</b></div>)}
        </div>
      )}
      {series.length > 1 && <div className="legendrow">{series.map((s, si) => <span key={s.name}><i style={{ background: PALETTE[si % 6] }} />{s.name}</span>)}</div>}
    </div>
  );
}
