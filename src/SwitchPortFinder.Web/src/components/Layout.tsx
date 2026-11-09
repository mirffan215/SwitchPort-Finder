import { NavLink, Outlet } from 'react-router-dom';
import SearchBox from './SearchBox';

const NAV = [
  { to: '/', label: 'Dashboard', end: true }, { to: '/switches', label: 'Switches' }, { to: '/devices', label: 'Devices' },
  { to: '/topology', label: 'Topology' }, { to: '/discovery', label: 'Discovery' }, { to: '/settings', label: 'Settings' },
];

export default function Layout() {
  return (
    <div className="app">
      <header className="header">
        <div className="brand">SwitchPort Finder</div>
        <SearchBox />
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
