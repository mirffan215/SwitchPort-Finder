import type { ReactNode } from 'react';
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import SearchBox from './SearchBox';
import ThemeSwitch from './ThemeSwitch';
import { useAuth } from './Auth';
import { api } from '../api';
import { ago } from './Bits';

const I = (d: string): ReactNode => <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d={d} /></svg>;
const NAV = [
  { to: '/', label: 'Dashboard', end: true, icon: I('M3 12l9-9 9 9M5 10v10h5v-6h4v6h5V10') },
  { to: '/switches', label: 'Switches', icon: I('M3 6h18v5H3zM3 13h18v5H3zM7 8.5h.01M7 15.5h.01') },
  { to: '/devices', label: 'Devices', icon: I('M4 5h16v11H4zM8 20h8M12 16v4') },
  { to: '/access-points', label: 'Access points', icon: I('M5 12.5a10 10 0 0114 0M8.5 16a5 5 0 017 0M12 19.5h.01M2 9a14 14 0 0120 0') },
  { to: '/ports', label: 'Ports', icon: I('M6 3h12v8H6zM9 11v4M15 11v4M5 15h14v6H5z') },
  { to: '/reports', label: 'Reports', icon: I('M4 20V10M10 20V4M16 20v-7M22 20H2') },
  { to: '/topology', label: 'Topology', icon: I('M12 3v6M5 21v-6h14v6M12 9v6M3 21h4M17 21h4M10 21h4') },
  { to: '/discovery', label: 'Discovery', icon: I('M21 12a9 9 0 11-3-6.7L21 8M21 3v5h-5') },
  { to: '/settings', label: 'Settings', icon: I('M12 15a3 3 0 100-6 3 3 0 000 6zM19 12a7 7 0 00-.1-1l2-1.5-2-3.5-2.3 1a7 7 0 00-1.7-1L15 3h-4l-.4 3a7 7 0 00-1.7 1l-2.3-1-2 3.5 2 1.5a7 7 0 000 2l-2 1.5 2 3.5 2.3-1a7 7 0 001.7 1l.4 3h4l.4-3a7 7 0 001.7-1l2.3 1 2-3.5-2-1.5a7 7 0 00.1-1z') },
];
const TITLES: [string, string][] = [['/switches', 'Switch Inventory & Port Matrix'], ['/devices', 'Devices'], ['/access-points', 'Access Points'], ['/ports', 'Ports'], ['/reports', 'Reports'], ['/topology', 'Network Topology'], ['/discovery', 'Discovery'], ['/settings', 'Settings'], ['/search', 'Search Results']];
const titleFor = (path: string) => (path === '/' ? 'Dashboard & Overview' : TITLES.find(([p]) => path.startsWith(p))?.[1] ?? 'SwitchPort Finder');

export default function Layout() {
  const { me, logout } = useAuth();
  const { pathname } = useLocation();
  const nav = useNavigate();
  // Back goes to the previous page in this tab's history; with no history (page opened directly) it goes to the parent section.
  const goBack = () => { if (window.history.state && window.history.state.idx > 0) nav(-1); else nav(pathname.split('/').length > 2 ? pathname.split('/').slice(0, 2).join('/') : '/'); };
  const { data: d } = useQuery({ queryKey: ['dashboard'], queryFn: api.dashboard, refetchInterval: 15_000 });
  const last = d?.lastDiscovery;
  const al = useQuery({ queryKey: ['alerts'], queryFn: api.alerts, refetchInterval: 30_000 });
  const name = me.username ?? 'Local user';
  return (
    <div className="app">
      <aside className="sidebar">
        <div className="brandbox">
          <div className="logo">{I('M12 3v6M5 21v-6h14v6M12 9v6M3 21h4M17 21h4M10 21h4')}</div>
          <div><div className="brandname">SwitchPort Finder</div><div className="brandsub">Port Finder &amp; SNMP Suite</div></div>
        </div>
        <div className="navgroup">Operations</div>
        <nav className="sidenav" aria-label="Main">
          {NAV.map((n) => <NavLink key={n.to} to={n.to} end={n.end} className={({ isActive }) => (isActive ? 'active' : '')}>{n.icon}{n.label}</NavLink>)}
        </nav>
        <div className="navgroup">System status</div>
        <div className="sysbox">
          <div><span>Switches</span><span className="v">{d?.switches ?? '—'}</span></div>
          <div><span>Devices</span><span className="v">{d?.devices ?? '—'}</span></div>
          <div><span>Active alerts</span><span className="v" style={{ color: al.data?.activeCount ? 'var(--bad)' : undefined }}>{al.data?.activeCount ?? '—'}</span></div>
          <div><span>Last discovery</span><span className="v">{last ? ago(last.completedAt ?? last.queuedAt) : '—'}</span></div>
        </div>
        <div className="userbox">
          <div className="avatar">{name.slice(0, 2).toUpperCase()}</div>
          <div className="who"><div>{name}</div>{me.authRequired && <div className="role">{me.role}</div>}</div>
          {me.authRequired && <button className="link" onClick={logout}>Sign out</button>}
        </div>
      </aside>
      <div className="content">
        <header className="topbar">
          {pathname !== '/' && <button type="button" className="btn backbtn" onClick={goBack} aria-label="Go back"><svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M15 18l-6-6 6-6" /></svg> Back</button>}
          <h1>{titleFor(pathname)}</h1>
          <SearchBox />
          <ThemeSwitch />
        </header>
        <main className="main"><Outlet /></main>
      </div>
    </div>
  );
}
