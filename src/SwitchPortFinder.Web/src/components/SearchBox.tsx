import { FormEvent, useEffect, useRef, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Link, useNavigate } from 'react-router-dom';
import { api } from '../api';

/** Header search: debounced, grouped quick results (Devices / Switches / Ports); Enter opens the full results page. */
export default function SearchBox() {
  const [q, setQ] = useState(''); const [dq, setDq] = useState(''); const [open, setOpen] = useState(false);
  const nav = useNavigate(); const box = useRef<HTMLDivElement>(null);
  useEffect(() => { const t = setTimeout(() => setDq(q.trim()), 250); return () => clearTimeout(t); }, [q]);
  useEffect(() => {
    const close = (e: MouseEvent) => { if (!box.current?.contains(e.target as Node)) setOpen(false); };
    document.addEventListener('mousedown', close); return () => document.removeEventListener('mousedown', close);
  }, []);
  const { data, isFetching } = useQuery({ queryKey: ['search', dq], queryFn: () => api.search(dq), enabled: dq.length >= 2, staleTime: 10_000 });
  const submit = (e: FormEvent) => { e.preventDefault(); if (q.trim()) { setOpen(false); nav(`/search?q=${encodeURIComponent(q.trim())}`); } };
  const done = () => { setOpen(false); setQ(''); };
  const empty = data && !data.devices.length && !data.switches.length && !data.ports.length;
  return (
    <div className="search" ref={box} role="search">
      <form onSubmit={submit}>
        <input value={q} onChange={(e) => { setQ(e.target.value); setOpen(true); }} onFocus={() => setOpen(true)} onKeyDown={(e) => e.key === 'Escape' && setOpen(false)}
          placeholder="Search MAC, IP, hostname or switch..." aria-label="Global search" />
      </form>
      {open && dq.length >= 2 && (
        <div className="dropdown">
          {isFetching && !data && <div className="muted pad">Searching…</div>}
          {empty && <div className="muted pad">No results for “{dq}”.</div>}
          {!!data?.devices.length && <div className="grp">Devices</div>}
          {data?.devices.slice(0, 5).map((d) => (
            <Link key={d.id} className="hit" to={d.switchId ? `/switches/${d.switchId}?port=${d.ifIndex}` : `/devices/${d.id}`} onClick={done}>
              <strong>{d.hostname}</strong> <span className="muted">{d.ip ?? 'IP unknown'} · <span className="mono">{d.mac}</span></span>
              <div className="muted small">{d.switchName ? `${d.switchName} · ${d.portName}` : 'location not determined'}</div>
            </Link>
          ))}
          {!!data?.switches.length && <div className="grp">Switches</div>}
          {data?.switches.slice(0, 3).map((s) => <Link key={s.id} className="hit" to={`/switches/${s.id}`} onClick={done}><strong>{s.name}</strong> <span className="muted">{s.managementIp}</span></Link>)}
          {!!data?.ports.length && <div className="grp">Ports</div>}
          {data?.ports.slice(0, 4).map((p) => <Link key={p.portId} className="hit" to={`/switches/${p.switchId}?port=${p.ifIndex}`} onClick={done}><strong>{p.switchName}</strong> · {p.portName} <span className="muted">{p.type}</span></Link>)}
          {data && !empty && <Link className="hit all" to={`/search?q=${encodeURIComponent(dq)}`} onClick={done}>See all results →</Link>}
        </div>
      )}
    </div>
  );
}
