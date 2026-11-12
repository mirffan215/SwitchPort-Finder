import { useEffect, useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { api, MANUFACTURERS } from '../api';
import { ErrorBox, ago, fmtDate } from '../components/Bits';
import { useAuth } from '../components/Auth';

/** The "⋯" menu of a row: closes on Escape, on an outside click and after a choice. */
function RowMenu({ label, children }: { label: string; children: (close: () => void) => React.ReactNode }) {
  const [open, setOpen] = useState(false); const box = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) return;
    const away = (e: MouseEvent) => { if (!box.current?.contains(e.target as Node)) setOpen(false); };
    const esc = (e: KeyboardEvent) => { if (e.key === 'Escape') setOpen(false); };
    document.addEventListener('mousedown', away); document.addEventListener('keydown', esc);
    return () => { document.removeEventListener('mousedown', away); document.removeEventListener('keydown', esc); };
  }, [open]);
  return (
    <div className="rowmenu" ref={box}>
      <button type="button" className="iconbtn" aria-haspopup="menu" aria-expanded={open} aria-label={label} onClick={() => setOpen(!open)}>⋯</button>
      {open && <div className="rowmenu-list" role="menu">{children(() => setOpen(false))}</div>}
    </div>
  );
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
  const del = useMutation({ mutationFn: api.deleteSwitch, onSuccess: () => qc.invalidateQueries({ queryKey: ['switches'] }) });
  const test = useMutation({ mutationFn: api.testSaved, onSuccess: () => qc.invalidateQueries({ queryKey: ['switches'] }) });
  const scan = useMutation({ mutationFn: api.scan, onSuccess: () => { setNote('Scan queued — follow it on the Discovery page.'); setTimeout(() => qc.invalidateQueries({ queryKey: ['switches'] }), 3000); } });
  const scanAll = useMutation({ mutationFn: api.scanAll, onSuccess: (r) => setNote(`${r.jobIds.length} scan(s) queued — follow them on the Discovery page.`) });
  const [note, setNote] = useState('');

  const rows = (data ?? []).filter((s) =>
    (!vendor || s.manufacturer === vendor) &&
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
        {canWrite && <button className="btn" disabled={scanAll.isPending || (data?.length ?? 0) === 0} onClick={() => scanAll.mutate()}>Scan all switches</button>}
        {canWrite && <Link className="btn" to="/switches/new?controller=1">+ Add Wireless Controller</Link>}
        {canWrite && <Link className="btn primary" to="/switches/new">+ Add Switch</Link>}
      </div>
      <ErrorBox error={error ?? del.error ?? test.error ?? scan.error ?? scanAll.error} />
      {note && <div className="alert success">{note} <Link to="/discovery">Open Discovery</Link></div>}
      {isLoading ? <p>Loading…</p> : (
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
                  <td className="num">{s.portCount || '—'}</td>
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
