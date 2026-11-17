import { useEffect, useState } from 'react';
import { useQuery, keepPreviousData } from '@tanstack/react-query';
import { Link, useSearchParams } from 'react-router-dom';
import { api, CATEGORIES, categoryLabel } from '../api';
import { CategoryBadge, ErrorBox, IconLink, PageHead, RowMenu, StateDot } from '../components/Bits';
import { useAuth } from '../components/Auth';

const COLS: [string, string][] = [['hostname', 'Device'], ['ip', 'Address'], ['category', 'Type'], ['switch', 'Location'], ['lastseen', 'Status'], ['', '']];

export default function DevicesPage() {
  const { canWrite } = useAuth();
  const [q, setQ] = useState(''); const [dq, setDq] = useState('');
  const [page, setPage] = useState(1); const [size, setSize] = useState(50);
  const [sort, setSort] = useState('lastseen'); const [desc, setDesc] = useState(true);
  const [sp, setSp] = useSearchParams();
  const portId = sp.get('portId') ?? ''; const excludeId = sp.get('excludeId') ?? ''; const behind = sp.get('behind') ?? '';
  const [state, setState] = useState(sp.get('state') ?? ''); const [category, setCategory] = useState(sp.get('category') ?? '');
  useEffect(() => { const t = setTimeout(() => { setDq(q); setPage(1); }, 300); return () => clearTimeout(t); }, [q]);   // debounce
  const { data, error, isFetching } = useQuery({
    queryKey: ['devices', dq, page, size, sort, desc, state, category, portId, excludeId],
    queryFn: () => api.devices({ q: dq, page, pageSize: size, sort, desc, state, category, portId: portId || undefined, excludeId: excludeId || undefined }),
    placeholderData: keepPreviousData,
  });
  // Export uses exactly the filters and sorting currently applied (never the page).
  const exportHref = (fmt: string) => `/api/export/devices?${new URLSearchParams({ format: fmt, ...(dq ? { q: dq } : {}), ...(state ? { state } : {}), ...(category ? { category } : {}), ...(portId ? { portId } : {}), ...(excludeId ? { excludeId } : {}), sort, desc: String(desc) })}`;
  const pages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;
  const th = (k: string, label: string) => k
    ? <th key={label} className="sortable" onClick={() => { if (sort === k) setDesc(!desc); else { setSort(k); setDesc(false); } }}>{label}{sort === k ? (desc ? ' ▼' : ' ▲') : ''}</th>
    : <th key={label}>{label}</th>;
  return (
    <section>
      <PageHead icon="M3 5h18v11H3zM8 20h8M12 16v4" title="Devices" subtitle={data ? `${data.total.toLocaleString()} device${data.total === 1 ? '' : 's'}${state ? ` · ${state === 'Active' ? 'online' : state.toLowerCase()}` : ''}` : 'Loading…'}>
        {canWrite && <Link className="btn primary" to="/devices/new">+ Add device</Link>}<a className="btn" href={exportHref('csv')} download>Export CSV</a><a className="btn" href={exportHref('xlsx')} download>Export Excel</a>
      </PageHead>
      <div className="toolbar" style={{ gap: 10 }}>
        <input placeholder="Search hostname, IP, MAC, manufacturer, switch, port, VLAN" value={q} onChange={(e) => setQ(e.target.value)} style={{ flex: '1 1 320px', minWidth: 0 }} aria-label="Search devices" />
        <select value={category} onChange={(e) => { setCategory(e.target.value); setPage(1); }} aria-label="Type"><option value="">All types</option>{CATEGORIES.map((c) => <option key={c} value={c}>{categoryLabel(c)}</option>)}</select>
        <div className="pills" role="group" aria-label="State">
          {[['', 'All'], ['Active', 'Online'], ['Offline', 'Offline'], ['Stale', 'Stale']].map(([v, l]) => <button key={v} type="button" className="pill-card" aria-pressed={state === v} onClick={() => { setState(v); setPage(1); }}>{l}</button>)}
        </div>
      </div>
      {portId && <div className="fchips"><button className="chipbtn" onClick={() => { const n = new URLSearchParams(sp); ['portId', 'excludeId', 'behind'].forEach((k) => n.delete(k)); setSp(n); }} title="Show all devices again">Devices behind {behind || 'this port'}</button></div>}
      <ErrorBox error={error} />
      <div className="cardlist-wrap" style={{ opacity: isFetching ? 0.7 : 1 }}>
        <table className="cardlist">
          <thead><tr>{COLS.map(([k, l]) => th(k, l))}</tr></thead>
          <tbody>
            {data?.items.map((d) => (
              <tr key={d.id}>
                <td className="c-main">
                  <div className="swname"><Link className="rowlink" to={`/devices/${d.id}`}>{d.hostname}</Link>{d.manual && <span className="tag" title="Added by hand">manual</span>}</div>
                  <div className="muted small" title={d.vendor === 'Private (randomized MAC)' ? 'Phones and laptops use a random MAC address for Wi-Fi privacy, so the manufacturer cannot be known from it.' : 'The maker of the network adapter, from the MAC address'}>{d.vendor ?? 'Manufacturer not looked up yet'}</div>
                </td>
                <td className="c-sub"><div>{d.ip ?? <span className="muted">Unknown IP</span>}</div><div className="mono muted small">{d.mac}</div></td>
                <td className="c-meta"><CategoryBadge c={d.category} manual={d.categoryManual} /></td>
                <td className="c-meta">{d.switchId ? <>
                  <div><Link to={`/switches/${d.switchId}`}>{d.switchName}</Link>{d.pinned && <span title="Location set manually"> 📌</span>}</div>
                  <div className="muted small">{d.ifIndex ? <Link to={`/switches/${d.switchId}?port=${d.ifIndex}`}>{d.portName}</Link> : '—'}{d.vlan != null && <span className="vlanchip">VLAN {d.vlan}</span>}</div></>
                  : <span className="muted" title="Only seen behind switch uplinks so far: open the device and use Edit details to set where it is plugged in.">Not located yet</span>}</td>
                <td className="c-status"><StateDot state={d.state} lastSeen={d.lastSeen} /></td>
                <td className="rowtools"><div className="tools">
                  <IconLink to={`/devices/${d.id}`} label={`View ${d.hostname}`} d="M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12zM12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6z" />
                  {d.switchId && d.ifIndex ? <IconLink to={`/switches/${d.switchId}?port=${d.ifIndex}`} label={`Open port ${d.portName}`} d="M3 6h18v5H3zM3 13h18v5H3zM7 8.5h.01M7 15.5h.01" /> : null}
                  <span className="more"><RowMenu label={`More actions for ${d.hostname}`}>{(close) => <>
                    <Link role="menuitem" to={`/devices/${d.id}`} onClick={close}>View details</Link>
                    <Link role="menuitem" to={`/devices/${d.id}/trace`} onClick={close}>Trace path</Link>
                    {d.switchId && <Link role="menuitem" to={`/switches/${d.switchId}`} onClick={close}>Open switch</Link>}
                    <button type="button" role="menuitem" onClick={() => { close(); navigator.clipboard?.writeText(d.mac).catch(() => undefined); }}>Copy MAC</button>
                    {d.ip && <button type="button" role="menuitem" onClick={() => { close(); navigator.clipboard?.writeText(d.ip!).catch(() => undefined); }}>Copy IP</button>}
                  </>}</RowMenu></span>
                </div></td>
              </tr>
            ))}
            {data?.items.length === 0 && <tr><td colSpan={6} className="empty-row">No devices. Scan a switch to discover devices.</td></tr>}
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
