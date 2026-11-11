import { useEffect, useState } from 'react';
import { useQuery, keepPreviousData } from '@tanstack/react-query';
import { Link, useSearchParams } from 'react-router-dom';
import { api } from '../api';
import { ErrorBox, StateBadge, ago } from '../components/Bits';
import { useAuth } from '../components/Auth';

const COLS: [string, string][] = [['hostname', 'Hostname'], ['ip', 'IP'], ['mac', 'MAC'], ['vendor', 'Product / Manufacturer'], ['switch', 'Switch'], ['', 'Port'], ['vlan', 'VLAN'], ['', 'Status'], ['lastseen', 'Last Seen']];

export default function DevicesPage() {
  const { canWrite } = useAuth();
  const [q, setQ] = useState(''); const [dq, setDq] = useState('');
  const [page, setPage] = useState(1); const [size, setSize] = useState(50);
  const [sort, setSort] = useState('lastseen'); const [desc, setDesc] = useState(true);
  const [sp] = useSearchParams();
  const [state, setState] = useState(sp.get('state') ?? '');
  useEffect(() => { const t = setTimeout(() => { setDq(q); setPage(1); }, 300); return () => clearTimeout(t); }, [q]);   // debounce
  const { data, error, isFetching } = useQuery({
    queryKey: ['devices', dq, page, size, sort, desc, state],
    queryFn: () => api.devices({ q: dq, page, pageSize: size, sort, desc, state }),
    placeholderData: keepPreviousData,
  });
  // Export uses exactly the filters and sorting currently applied (never the page).
  const exportHref = (fmt: string) => `/api/export/devices?${new URLSearchParams({ format: fmt, ...(dq ? { q: dq } : {}), ...(state ? { state } : {}), sort, desc: String(desc) })}`;
  const pages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;
  const th = (k: string, label: string) => k
    ? <th key={label} className="sortable" onClick={() => { if (sort === k) setDesc(!desc); else { setSort(k); setDesc(false); } }}>{label}{sort === k ? (desc ? ' ▼' : ' ▲') : ''}</th>
    : <th key={label}>{label}</th>;
  return (
    <section>
      <div className="toolbar">
        <h1>Devices</h1><div className="spacer" />
        <input placeholder="Search hostname, IP, MAC, manufacturer, switch, port, VLAN" value={q} onChange={(e) => setQ(e.target.value)} style={{ minWidth: 340 }} aria-label="Search devices" />
        {canWrite && <Link className="btn primary" to="/devices/new">+ Add device</Link>}<a className="btn" href={exportHref('csv')} download>Export CSV</a><a className="btn" href={exportHref('xlsx')} download>Export Excel</a>
        <select value={state} onChange={(e) => { setState(e.target.value); setPage(1); }} aria-label="State"><option value="">All states</option><option>Active</option><option>Offline</option><option>Stale</option></select>
      </div>
      <ErrorBox error={error} />
      <div className="tablewrap" style={{ opacity: isFetching ? 0.7 : 1 }}>
        <table>
          <thead><tr>{COLS.map(([k, l]) => th(k, l))}</tr></thead>
          <tbody>
            {data?.items.map((d) => (
              <tr key={d.id}>
                <td><Link to={`/devices/${d.id}`}>{d.hostname}</Link>{d.manual && <span className="tag" title="Added by hand">manual</span>}</td><td>{d.ip ?? <span className="muted">Unknown</span>}</td><td className="mono">{d.mac}</td><td>{d.vendor ? <span title={d.vendor === 'Private (randomized MAC)' ? 'Phones and laptops use a random MAC address for Wi-Fi privacy, so the manufacturer cannot be known from it.' : 'Manufacturer of the network adapter, from the MAC address (IEEE registry).'}>{d.vendor}</span> : <span className="muted" title="This MAC address prefix is not in the IEEE registry.">Unknown</span>}</td>
                <td>{d.switchId ? <><Link to={`/switches/${d.switchId}`}>{d.switchName}</Link>{d.pinned && <span title="Location set manually"> 📌</span>}</> : <span title="Only seen behind switch uplinks so far: open the device and use Edit details to set where it is plugged in.">—</span>}</td>
                <td>{d.switchId && d.ifIndex ? <Link to={`/switches/${d.switchId}?port=${d.ifIndex}`}>{d.portName}</Link> : '—'}</td>
                <td>{d.vlan ?? '—'}</td><td><StateBadge state={d.state} /></td><td>{ago(d.lastSeen)}</td>
              </tr>
            ))}
            {data?.items.length === 0 && <tr><td colSpan={9} className="muted">No devices. Scan a switch to discover devices.</td></tr>}
          </tbody>
        </table>
      </div>
      <div className="toolbar" style={{ marginTop: 10 }}>
        <span className="muted">{data?.total ?? 0} devices</span><div className="spacer" />
        <select value={size} onChange={(e) => { setSize(Number(e.target.value)); setPage(1); }} aria-label="Page size">{[25, 50, 100, 200].map((n) => <option key={n} value={n}>{n} / page</option>)}</select>
        <button className="btn" disabled={page <= 1} onClick={() => setPage(page - 1)}>‹ Prev</button>
        <span>Page {page} of {pages}</span>
        <button className="btn" disabled={page >= pages} onClick={() => setPage(page + 1)}>Next ›</button>
      </div>
    </section>
  );
}
