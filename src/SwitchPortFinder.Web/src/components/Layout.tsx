import type { ReactNode } from 'react';
import { NavLink, Outlet, useLocation } from 'react-router-dom';
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
  { to: '/topology', label: 'Topology', icon: I('M12 3v6M5 21v-6h14v6M12 9v6M3 21h4M17 21h4M10 21h4') },
  { to: '/discovery', label: 'Discovery', icon: I('M21 12a9 9 0 11-3-6.7L21 8M21 3v5h-5') },
  { to: '/settings', label: 'Settings', icon: I('M12 15a3 3 0 100-6 3 3 0 000 6zM19 12a7 7 0 00-.1-1l2-1.5-2-3.5-2.3 1a7 7 0 00-1.7-1L15 3h-4l-.4 3a7 7 0 00-1.7 1l-2.3-1-2 3.5 2 1.5a7 7 0 000 2l-2 1.5 2 3.5 2.3-1a7 7 0 001.7 1l.4 3h4l.4-3a7 7 0 001.7-1l2.3 1 2-3.5-2-1.5a7 7 0 00.1-1z') },
];
const TITLES: [string, string][] = [['/switches', 'Switch Inventory & Port Matrix'], ['/devices', 'Devices'], ['/topology', 'Network Topology'], ['/discovery', 'Discovery'], ['/settings', 'Settings'], ['/search', 'Search Results']];
const titleFor = (path: string) => (path === '/' ? 'Dashboard & Overview' : TITLES.find(([p]) => path.startsWith(p))?.[1] ?? 'SwitchPort Finder');

export default function Layout() {
  const { me, logout } = useAuth();
  const { pathname } = useLocation();
  const { data: d } = useQuery({ queryKey: ['dashboard'], queryFn: api.dashboard, refetchInterval: 15_000 });
  const last = d?.lastDiscovery;
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
          <h1>{titleFor(pathname)}</h1>
          <SearchBox />
          <ThemeSwitch />
        </header>
        <main className="main"><Outlet /></main>
      </div>
    </div>
  );
}
