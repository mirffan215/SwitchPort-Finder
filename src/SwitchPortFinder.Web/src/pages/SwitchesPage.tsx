import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { api, MANUFACTURERS, PRODUCT_TYPES, type ProductType } from '../api';
import { ErrorBox, IconLink, PageHead, RowMenu, ago, fmtDate } from '../components/Bits';
import { useAuth } from '../components/Auth';

function PortsBar({ total, up }: { total: number; up: number }) {
  if (!total) return <span>—</span>;
  return <div className="portsbar" title={`${up} of ${total} ports are up`}><span className="utilbar"><i style={{ width: `${Math.round((up / total) * 100)}%` }} /></span><span><strong>{up}</strong><span className="muted"> / {total}</span></span></div>;
}

function TypeChip({ t }: { t?: ProductType }) {
  const p = PRODUCT_TYPES.find((x) => x.value === (t ?? 'Switch')) ?? PRODUCT_TYPES[0];
  return <span className={`typechip ${p.value}`} title={p.hint}><svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d={p.icon} /></svg>{p.label}</span>;
}

const STALE_MS = 24 * 3600 * 1000;
function StatusDot({ status, lastScanAt }: { status: string; lastScanAt?: string | null }) {
  const stale = !!lastScanAt && Date.now() - new Date(lastScanAt).getTime() > STALE_MS;
  const cls = status === 'Unreachable' ? 'bad' : status === 'Reachable' ? (stale ? 'warn' : 'ok') : 'muted';
  return (
    <div className="statuscell">
      <div className="statusline"><span className={`stdot ${cls}`} aria-hidden="true" /><span>{status}</span></div>
      <div className="muted small" title={fmtDate(lastScanAt)}>{lastScanAt ? `scanned ${ago(lastScanAt)}${stale ? ' · stale' : ''}` : 'never scanned'}</div>
    </div>
  );
}

export default function SwitchesPage() {
  const qc = useQueryClient();
  const { canWrite } = useAuth();
  const { data, isLoading, error } = useQuery({ queryKey: ['switches'], queryFn: api.listSwitches });
  const [vendor, setVendor] = useState('');
  const [filter, setFilter] = useState('');
  const [stat, setStat] = useState('');
  const [ptype, setPtype] = useState<'' | ProductType>('');
  const [view, setView] = useState<'table' | 'cards'>(() => { try { return localStorage.getItem('spf.switchview') === 'cards' ? 'cards' : 'table'; } catch { return 'table'; } });
  const pick = (v: 'table' | 'cards') => { setView(v); try { localStorage.setItem('spf.switchview', v); } catch { /* the choice just lasts for this page */ } };
  const del = useMutation({ mutationFn: api.deleteSwitch, onSuccess: () => qc.invalidateQueries({ queryKey: ['switches'] }) });
  const test = useMutation({ mutationFn: api.testSaved, onSuccess: () => qc.invalidateQueries({ queryKey: ['switches'] }) });
  const scan = useMutation({ mutationFn: api.scan, onSuccess: () => { setNote('Scan queued — follow it on the Discovery page.'); setTimeout(() => qc.invalidateQueries({ queryKey: ['switches'] }), 3000); } });
  const scanAll = useMutation({ mutationFn: api.scanAll, onSuccess: (r) => setNote(`${r.jobIds.length} scan(s) queued — follow them on the Discovery page.`) });
  const [note, setNote] = useState('');

  const rows = (data ?? []).filter((s) =>
    (!vendor || s.manufacturer === vendor) && (!stat || s.status === stat) && (!ptype || (s.productType ?? 'Switch') === ptype) &&
    (!filter || `${s.name} ${s.managementIp}`.toLowerCase().includes(filter.toLowerCase())));

  return (
    <section>
      <PageHead icon="M3 6h18v5H3zM3 13h18v5H3zM7 8.5h.01M7 15.5h.01" title="Network inventory" subtitle={`${rows.length} of ${data?.length ?? 0} entr${(data?.length ?? 0) === 1 ? 'y' : 'ies'}`}>
        {canWrite && <button className="btn" disabled={scanAll.isPending || (data?.length ?? 0) === 0} onClick={() => scanAll.mutate()}>Scan all</button>}
        {canWrite && <Link className="btn" to="/switches/new?controller=1">+ Add Wireless Controller</Link>}
        {canWrite && <Link className="btn primary" to="/switches/new">+ Add Switch</Link>}
      </PageHead>
      <div className="toolbar" style={{ gap: 10 }}>
        <input placeholder="Filter name / IP" value={filter} onChange={(e) => setFilter(e.target.value)} />
        <select value={vendor} onChange={(e) => setVendor(e.target.value)} aria-label="Manufacturer filter">
          <option value="">All manufacturers</option>
          {MANUFACTURERS.map((m) => <option key={m.value} value={m.value}>{m.label}</option>)}
        </select>
        <select value={ptype} onChange={(e) => setPtype(e.target.value as '' | ProductType)} aria-label="Product type filter"><option value="">All product types</option>{PRODUCT_TYPES.map((p) => <option key={p.value} value={p.value} title={p.hint}>{p.plural}</option>)}</select>
        <select value={stat} onChange={(e) => setStat(e.target.value)} aria-label="Status filter"><option value="">All statuses</option><option value="Reachable">Reachable</option><option value="Unreachable">Unreachable</option><option value="Unknown">Never scanned</option></select>
        <div className="seg" role="group" aria-label="View"><button type="button" className={view === 'table' ? 'on' : ''} aria-pressed={view === 'table'} onClick={() => pick('table')}>Table</button><button type="button" className={view === 'cards' ? 'on' : ''} aria-pressed={view === 'cards'} onClick={() => pick('cards')}>Cards</button></div>
      </div>
      <ErrorBox error={error ?? del.error ?? test.error ?? scan.error ?? scanAll.error} />
      {note && <div className="alert success">{note} <Link to="/discovery">Open Discovery</Link></div>}
      {isLoading ? <p>Loading…</p> : view === 'cards' ? (
        <div className="swcards">
          {rows.map((s) => (
            <article key={s.id} className="swcard">
              <div className="swname"><Link to={`/switches/${s.id}`}>{s.name}</Link>{s.useAsArpSource && <span className="tag" title="Provides the IP-to-MAC (ARP) table">ARP</span>}</div>
              <div><TypeChip t={s.productType} /></div>
              <div className="muted small">{s.model ?? 'Model not read yet'} · {s.manufacturerName.replace(/\s*\(detected\)/i, '')}</div>
              <div className="mono">{s.managementIp}</div>
              <StatusDot status={s.status} lastScanAt={s.lastScanAt} />
              <PortsBar total={s.portCount} up={s.portsUp ?? 0} />
              <div className="cardactions">{canWrite && <button type="button" className="btn small" disabled={scan.isPending} onClick={() => scan.mutate(s.id)}>Scan</button>}<Link className="btn small" to={`/switches/${s.id}`}>Open</Link>{canWrite && <Link className="btn small" to={`/switches/${s.id}/edit`}>Edit</Link>}</div>
            </article>
          ))}
          {rows.length === 0 && <p className="muted">No switches.</p>}
        </div>
      ) : (
        <div className="cardlist-wrap">
          <table className="cardlist">
            <thead><tr><th>Name</th><th title="Product type: what this entry is. A wireless controller lists access points instead of ports; a router provides the ARP table.">Type</th><th>Management IP</th><th>Manufacturer</th><th className="num">Ports</th><th>Status</th><th><span className="sr-only">Actions</span></th></tr></thead>
            <tbody>
              {rows.map((s) => (
                <tr key={s.id}>
                  <td className="c-main">
                    <div className="swname"><Link className="rowlink" to={`/switches/${s.id}`}>{s.name}</Link>
                      {s.useAsArpSource && <span className="tag" title="Provides the IP-to-MAC (ARP) table">ARP</span>}</div>
                    <div className="muted small">{s.model ?? 'Model not read yet'}</div>
                  </td>
                  <td className="c-meta"><TypeChip t={s.productType} /></td>
                  <td className="c-sub nowrap mono">{s.managementIp}</td>
                  <td className="c-meta"><span className="vendorchip" title={s.manufacturerName}>{s.manufacturerName.replace(/\s*\(detected\)/i, '')}</span></td>
                  <td className="c-meta num">{s.productType === 'WirelessController' ? <span className="muted" title="A controller lists access points, not a port matrix">—</span> : <PortsBar total={s.portCount} up={s.portsUp ?? 0} />}</td>
                  <td className="c-status"><StatusDot status={s.status} lastScanAt={s.lastScanAt} /></td>
                  <td className="rowtools"><div className="tools">
                    {canWrite && <button type="button" className="tool iconbtn" disabled={scan.isPending} onClick={() => scan.mutate(s.id)} aria-label={`Scan ${s.name} now`} title="Scan now"><svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M21 12a9 9 0 11-3-6.7L21 8M21 3v5h-5" /></svg></button>}
                    <IconLink to={`/switches/${s.id}`} label={`Open ${s.name}`} d="M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12zM12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6z" />
                    <span className="more"><RowMenu label={`More actions for ${s.name}`}>{(close) => <>
                      <Link role="menuitem" to={`/switches/${s.id}`} onClick={close}>Open</Link>
                      {canWrite && <button type="button" role="menuitem" disabled={scan.isPending} onClick={() => { close(); scan.mutate(s.id); }}>Scan now</button>}
                      {canWrite && <button type="button" role="menuitem" disabled={test.isPending} onClick={() => { close(); test.mutate(s.id); }}>Test SNMP connection</button>}
                      {canWrite && <Link role="menuitem" to={`/switches/${s.id}/edit`} onClick={close}>Edit</Link>}
                      {canWrite && <button type="button" role="menuitem" className="danger" onClick={() => { close(); if (confirm(`Delete ${s.name}? Its ports and discovered data are removed.`)) del.mutate(s.id); }}>Delete…</button>}
                    </>}</RowMenu></span>
                  </div></td>
                </tr>
              ))}
              {rows.length === 0 && <tr><td colSpan={7} className="empty-row">No devices match.</td></tr>}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
