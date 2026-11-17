import { useEffect, useState } from 'react';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { Link, useSearchParams } from 'react-router-dom';
import { api } from '../api';
import { Dot, ErrorBox, PageHead, PortTypeBadge } from '../components/Bits';
import { fmtBps } from '../components/Charts';

const COLS: [string, string][] = [['switch', 'Switch'], ['name', 'Port'], ['', 'Status'], ['', 'Type'], ['', 'Device'], ['speed', 'Speed'], ['vlan', 'VLAN'], ['util', 'Utilization']];

/** Every port on every switch. The dashboard's Connected Ports / Switch Uplinks cards and the port-state donut land here, pre-filtered. */
export default function PortsPage() {
  const [sp, setSp] = useSearchParams();
  const status = sp.get('status') ?? ''; const type = sp.get('type') ?? '';
  const [q, setQ] = useState(''); const [dq, setDq] = useState('');
  const [page, setPage] = useState(1); const [size, setSize] = useState(50);
  const [sort, setSort] = useState('switch'); const [desc, setDesc] = useState(false);
  useEffect(() => { const t = setTimeout(() => { setDq(q); setPage(1); }, 300); return () => clearTimeout(t); }, [q]);
  const { data, error, isFetching } = useQuery({ queryKey: ['ports', dq, status, type, page, size, sort, desc], queryFn: () => api.allPorts({ q: dq, status, type, page, pageSize: size, sort, desc }), placeholderData: keepPreviousData });
  const set = (k: string, v: string) => { const n = new URLSearchParams(sp); if (v) n.set(k, v); else n.delete(k); setSp(n, { replace: true }); setPage(1); };
  const pages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;
  const th = (k: string, l: string) => k ? <th key={l} className="sortable" onClick={() => { if (sort === k) setDesc(!desc); else { setSort(k); setDesc(false); } }}>{l}{sort === k ? (desc ? ' ▼' : ' ▲') : ''}</th> : <th key={l}>{l}</th>;
  return (
    <section>
      <PageHead icon="M6 3h12v8H6zM9 11v4M15 11v4M5 15h14v6H5z" title="Ports" subtitle={data ? `${data.total.toLocaleString()} port${data.total === 1 ? '' : 's'}` : 'Loading…'} />
      <div className="toolbar" style={{ gap: 10 }}>
        <input placeholder="Search port, description, switch, VLAN" aria-label="Search ports" value={q} onChange={(e) => setQ(e.target.value)} style={{ flex: '1 1 300px', minWidth: 0 }} />
        <select value={status} onChange={(e) => set('status', e.target.value)} aria-label="Status"><option value="">All statuses</option><option value="up">Connected (up)</option><option value="down">Down</option></select>
        <select value={type} onChange={(e) => set('type', e.target.value)} aria-label="Type"><option value="">All types</option><option value="Endpoint">Endpoint</option><option value="SwitchUplink">Switch uplink</option><option value="Unknown">Unknown</option><option value="Down">Down / unused</option></select>
      </div>
      <ErrorBox error={error} />
      <div className="cardlist-wrap" style={{ opacity: isFetching ? 0.7 : 1 }}>
        <table className="cardlist">
          <thead><tr>{COLS.map(([k, l]) => th(k, l))}</tr></thead>
          <tbody>
            {data?.items.map((p) => (
              <tr key={p.portId}>
                <td><Link to={`/switches/${p.switchId}`}>{p.switchName}</Link></td>
                <td className="c-main"><Link className="rowlink" to={`/switches/${p.switchId}?port=${p.ifIndex}`}>{p.name}</Link>{p.alias && <div className="muted small">{p.alias}</div>}</td>
                <td><Dot kind={p.status === 'UP' ? 'ok' : 'muted'} label={p.status === 'UP' ? 'Up' : 'Down'} /></td><td><PortTypeBadge type={p.type} /></td>
                <td>{p.device ?? (p.macCount > 0 ? <span className="muted">{p.macCount} MACs</span> : '—')}</td>
                <td>{p.speedMbps ? `${p.speedMbps >= 1000 ? p.speedMbps / 1000 + ' Gbps' : p.speedMbps + ' Mbps'}` : '—'}</td><td>{p.vlan ?? '—'}</td>
                <td>{p.traffic?.utilizationPct == null ? <span className="muted">—</span> : <span className="utilcell" title={`in ${fmtBps(p.traffic.inBps)}, out ${fmtBps(p.traffic.outBps)}`}><span className="utilbar"><i className={p.traffic.utilizationPct >= 80 ? 'bad' : p.traffic.utilizationPct >= 50 ? 'warn' : ''} style={{ width: `${Math.min(100, Math.max(2, p.traffic.utilizationPct))}%` }} /></span>{p.traffic.utilizationPct.toFixed(p.traffic.utilizationPct < 10 ? 1 : 0)}%</span>}</td>
              </tr>
            ))}
            {data?.items.length === 0 && <tr><td colSpan={8} className="empty-row">No ports match. Scan a switch to discover ports.</td></tr>}
          </tbody>
        </table>
      </div>
      <div className="toolbar" style={{ marginTop: 10 }}>
        <span className="muted">{data?.total ?? 0} ports</span><div className="spacer" />
        <select value={size} onChange={(e) => { setSize(Number(e.target.value)); setPage(1); }} aria-label="Page size">{[25, 50, 100, 200].map((n) => <option key={n} value={n}>{n} / page</option>)}</select>
        <button className="btn" disabled={page <= 1} onClick={() => setPage(page - 1)}>‹ Prev</button><span>Page {page} of {pages}</span>
        <button className="btn" disabled={page >= pages} onClick={() => setPage(page + 1)}>Next ›</button>
      </div>
    </section>
  );
}
