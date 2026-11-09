import { FormEvent, useState } from 'react';
import { NavLink, Outlet, useNavigate } from 'react-router-dom';

const NAV = [
  { to: '/', label: 'Dashboard', end: true }, { to: '/switches', label: 'Switches' }, { to: '/devices', label: 'Devices' },
  { to: '/topology', label: 'Topology' }, { to: '/discovery', label: 'Discovery' }, { to: '/settings', label: 'Settings' },
];

export default function Layout() {
  const [q, setQ] = useState('');
  const nav = useNavigate();
  const submit = (e: FormEvent) => { e.preventDefault(); if (q.trim()) nav(`/search?q=${encodeURIComponent(q.trim())}`); };
  return (
    <div className="app">
      <header className="header">
        <div className="brand">SwitchPort Finder</div>
        <form className="search" onSubmit={submit} role="search">
          <input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Search MAC, IP, hostname or switch..." aria-label="Global search" />
        </form>
      </header>
      <nav className="nav">
        {NAV.map((n) => (
          <NavLink key={n.to} to={n.to} end={n.end} className={({ isActive }) => (isActive ? 'active' : '')}>{n.label}</NavLink>
        ))}
      </nav>
      <main className="main"><Outlet /></main>
    </div>
  );
}
