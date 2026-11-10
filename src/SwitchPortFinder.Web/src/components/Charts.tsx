import { useState, type ReactNode } from 'react';

/** Bits per second -> "123.4 Mbps". */
export function fmtBps(b?: number | null): string {
  if (b == null) return '—';
  const u = ['bps', 'Kbps', 'Mbps', 'Gbps', 'Tbps']; let i = 0; let v = b;
  while (v >= 1000 && i < u.length - 1) { v /= 1000; i++; }
  return `${v >= 100 || i === 0 ? v.toFixed(0) : v.toFixed(1)} ${u[i]}`;
}
export const fmtRate = (v: number) => (v >= 10 ? v.toFixed(0) : v >= 1 ? v.toFixed(1) : v.toFixed(2));
const level = (pct: number) => (pct >= 85 ? 'bad' : pct >= 60 ? 'warn' : 'ok');

/** Semi-circular gauge. Interactive: focusable, hover/selected highlight, click handler. */
export function Gauge({ pct, label, sub, selected, onSelect, title }: { pct: number; label: string; sub?: string; selected?: boolean; onSelect?: () => void; title?: string }) {
  const v = Math.max(0, Math.min(100, pct));
  return (
    <button type="button" className={`gauge ${selected ? 'sel' : ''}`} onClick={onSelect} title={title} aria-pressed={selected} aria-label={`${label}: ${v.toFixed(0)} percent`}>
      <svg viewBox="0 0 120 72" role="img" aria-hidden="true">
        <path d="M 10 62 A 50 50 0 0 1 110 62" pathLength={100} className="g-track" />
        <path d="M 10 62 A 50 50 0 0 1 110 62" pathLength={100} className={`g-val ${level(v)}`} strokeDasharray={`${v} 100`} />
        <text x="60" y="56" textAnchor="middle" className="g-num">{v < 10 && v > 0 ? v.toFixed(1) : v.toFixed(0)}%</text>
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
