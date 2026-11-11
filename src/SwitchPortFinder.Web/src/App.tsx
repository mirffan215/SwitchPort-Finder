import { Navigate, Route, Routes } from 'react-router-dom';
import Layout from './components/Layout';
import Dashboard from './pages/Dashboard';
import SwitchesPage from './pages/SwitchesPage';
import SwitchFormPage from './pages/SwitchFormPage';
import SwitchPage from './pages/SwitchPage';
import SettingsPage from './pages/SettingsPage';
import TopologyPage from './pages/TopologyPage';
import DevicesPage from './pages/DevicesPage';
import DiscoveryPage from './pages/DiscoveryPage';
import DevicePage from './pages/DevicePage';
import PortsPage from './pages/PortsPage';
import SearchPage from './pages/SearchPage';
import TracePage from './pages/TracePage';

export default function App() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<Dashboard />} />
        <Route path="switches" element={<SwitchesPage />} />
        <Route path="switches/new" element={<SwitchFormPage />} />
        <Route path="switches/:id/edit" element={<SwitchFormPage />} />
        <Route path="switches/:id" element={<SwitchPage />} />
        <Route path="devices" element={<DevicesPage />} />
        <Route path="devices/:id" element={<DevicePage />} />
        <Route path="devices/:id/trace" element={<TracePage />} />
        <Route path="ports" element={<PortsPage />} />
        <Route path="topology" element={<TopologyPage />} />
        <Route path="discovery" element={<DiscoveryPage />} />
        <Route path="settings" element={<SettingsPage />} />
        <Route path="search" element={<SearchPage />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  );
}
