import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { api, MANUFACTURERS } from '../api';
import { ErrorBox, RowMenu, ago, fmtDate } from '../components/Bits';
import { useAuth } from '../components/Auth';

function PortsBar({ total, up }: { total: number; up: number }) {
  if (!total) return <span>—</span>;
  return <div className="portsbar" title={`${up} of ${total} ports are up`}><span className="utilbar"><i style={{ width: `${Math.round((up / total) * 100)}%` }} /></span><span><strong>{up}</strong><span className="muted"> / {total}</span></span></div>;
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
  const [view, setView] = useState<'table' | 'cards'>(() => { try { return localStorage.getItem('spf.switchview') === 'cards' ? 'cards' : 'table'; } catch { return 'table'; } });
  const pick = (v: 'table' | 'cards') => { setView(v); try { localStorage.setItem('spf.switchview', v); } catch { /* the choice just lasts for this page */ } };
  const del = useMutation({ mutationFn: api.deleteSwitch, onSuccess: () => qc.invalidateQueries({ queryKey: ['switches'] }) });
  const test = useMutation({ mutationFn: api.testSaved, onSuccess: () => qc.invalidateQueries({ queryKey: ['switches'] }) });
  const scan = useMutation({ mutationFn: api.scan, onSuccess: () => { setNote('Scan queued — follow it on the Discovery page.'); setTimeout(() => qc.invalidateQueries({ queryKey: ['switches'] }), 3000); } });
  const scanAll = useMutation({ mutationFn: api.scanAll, onSuccess: (r) => setNote(`${r.jobIds.length} scan(s) queued — follow them on the Discovery page.`) });
  const [note, setNote] = useState('');

  const rows = (data ?? []).filter((s) =>
    (!vendor || s.manufacturer === vendor) && (!stat || s.status === stat) &&
    (!filter || `${s.name} ${s.managementIp}`.toLowerCase().includes(filter.toLowerCase())));

  return (
    <section>
      <div className="toolbar">
        <h1>Switches</h1>
        <div className="spacer" />
        <input placeholder="Filter name / IP" value={filter} onChange={(e) => setFilter(e.target.value)} />
        <select value={vendor} onChange={(e) => setVendor(e.target.value)} aria-label="Manufacturer filter">
          <option value="">All manufacturers</option>
          {MANUFACTURERS.map((m) => <option key={m.value} value={m.value}>{m.label}</option>)}
        </select>
        <select value={stat} onChange={(e) => setStat(e.target.value)} aria-label="Status filter"><option value="">All statuses</option><option value="Reachable">Reachable</option><option value="Unreachable">Unreachable</option><option value="Unknown">Never scanned</option></select>
        <div className="seg" role="group" aria-label="View"><button type="button" className={view === 'table' ? 'on' : ''} aria-pressed={view === 'table'} onClick={() => pick('table')}>Table</button><button type="button" className={view === 'cards' ? 'on' : ''} aria-pressed={view === 'cards'} onClick={() => pick('cards')}>Cards</button></div>
        {canWrite && <button className="btn" disabled={scanAll.isPending || (data?.length ?? 0) === 0} onClick={() => scanAll.mutate()}>Scan all switches</button>}
        {canWrite && <Link className="btn" to="/switches/new?controller=1">+ Add Wireless Controller</Link>}
        {canWrite && <Link className="btn primary" to="/switches/new">+ Add Switch</Link>}
      </div>
      <ErrorBox error={error ?? del.error ?? test.error ?? scan.error ?? scanAll.error} />
      {note && <div className="alert success">{note} <Link to="/discovery">Open Discovery</Link></div>}
      {isLoading ? <p>Loading…</p> : view === 'cards' ? (
        <div className="swcards">
          {rows.map((s) => (
            <article key={s.id} className="swcard">
              <div className="swname"><Link to={`/switches/${s.id}`}>{s.name}</Link>{s.useAsArpSource && <span className="tag" title="Provides the IP-to-MAC (ARP) table">ARP</span>}{s.isWirelessController && <span className="tag" title="Wireless controller">WLC</span>}</div>
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
        <div className="tablewrap swtable">
          <table>
            <thead><tr><th>Switch</th><th>Management IP</th><th>Manufacturer</th><th className="num">Ports</th><th>Status</th><th><span className="sr-only">Actions</span></th></tr></thead>
            <tbody>
              {rows.map((s) => (
                <tr key={s.id}>
                  <td>
                    <div className="swname"><Link to={`/switches/${s.id}`}>{s.name}</Link>
                      {s.useAsArpSource && <span className="tag" title="Provides the IP-to-MAC (ARP) table">ARP</span>}{s.isWirelessController && <span className="tag" title="Wireless controller">WLC</span>}</div>
                    <div className="muted small">{s.model ?? 'Model not read yet'}</div>
                  </td>
                  <td className="nowrap mono">{s.managementIp}</td>
                  <td><span className="vendorchip" title={s.manufacturerName}>{s.manufacturerName.replace(/\s*\(detected\)/i, '')}</span>{s.manufacturerName.toLowerCase().includes('detected') && <span className="muted small" title="Detected from the switch"> auto</span>}{s.vendorMismatch && <span title="Selected manufacturer differs from detected vendor"> ⚠</span>}</td>
                  <td className="num"><PortsBar total={s.portCount} up={s.portsUp ?? 0} /></td>
                  <td><StatusDot status={s.status} lastScanAt={s.lastScanAt} /></td>
                  <td className="rowactions">
                    {canWrite && <button type="button" className="btn small" disabled={scan.isPending} onClick={() => scan.mutate(s.id)} title={`Scan ${s.name} now`}>Scan</button>}
                    <Link className="btn small" to={`/switches/${s.id}`}>Open</Link>
                    {canWrite && <RowMenu label={`More actions for ${s.name}`}>{(close) => <>
                      <button type="button" role="menuitem" disabled={test.isPending} onClick={() => { close(); test.mutate(s.id); }}>Test SNMP connection</button>
                      <Link role="menuitem" to={`/switches/${s.id}/edit`} onClick={close}>Edit</Link>
                      <button type="button" role="menuitem" className="danger" onClick={() => { close(); if (confirm(`Delete ${s.name}? Its ports and discovered data are removed.`)) del.mutate(s.id); }}>Delete…</button>
                    </>}</RowMenu>}
                  </td>
                </tr>
              ))}
              {rows.length === 0 && <tr><td colSpan={6} className="muted">No switches.</td></tr>}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
