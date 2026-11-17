import { Suspense, lazy } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom';
import Layout from './components/Layout';
import Dashboard from './pages/Dashboard';
const SwitchesPage = lazy(() => import('./pages/SwitchesPage'));
const SwitchFormPage = lazy(() => import('./pages/SwitchFormPage'));
const SwitchPage = lazy(() => import('./pages/SwitchPage'));
const SettingsPage = lazy(() => import('./pages/SettingsPage'));
const TopologyPage = lazy(() => import('./pages/TopologyPage'));
const DevicesPage = lazy(() => import('./pages/DevicesPage'));
const DiscoveryPage = lazy(() => import('./pages/DiscoveryPage'));
const DevicePage = lazy(() => import('./pages/DevicePage'));
const PortsPage = lazy(() => import('./pages/PortsPage'));
const ReportsPage = lazy(() => import('./pages/ReportsPage'));
const AccessPointsPage = lazy(() => import('./pages/AccessPointsPage'));
const AddDevicePage = lazy(() => import('./pages/AddDevicePage'));
const SearchPage = lazy(() => import('./pages/SearchPage'));
const TracePage = lazy(() => import('./pages/TracePage'));

export default function App() {
  return (
    <Suspense fallback={<p className="muted" style={{ padding: 24 }}>Loading…</p>}>
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<Dashboard />} />
        <Route path="switches" element={<SwitchesPage />} />
        <Route path="switches/new" element={<SwitchFormPage />} />
        <Route path="switches/:id/edit" element={<SwitchFormPage />} />
        <Route path="switches/:id" element={<SwitchPage />} />
        <Route path="devices" element={<DevicesPage />} />
        <Route path="devices/new" element={<AddDevicePage />} />
        <Route path="devices/:id" element={<DevicePage />} />
        <Route path="devices/:id/trace" element={<TracePage />} />
        <Route path="access-points" element={<AccessPointsPage />} />
        <Route path="reports" element={<ReportsPage />} />
        <Route path="ports" element={<PortsPage />} />
        <Route path="topology" element={<TopologyPage />} />
        <Route path="discovery" element={<DiscoveryPage />} />
        <Route path="settings" element={<SettingsPage />} />
        <Route path="search" element={<SearchPage />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
    </Suspense>
  );
}
