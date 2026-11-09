import { Navigate, Route, Routes } from 'react-router-dom';
import Layout from './components/Layout';
import Dashboard from './pages/Dashboard';
import SwitchesPage from './pages/SwitchesPage';
import SwitchFormPage from './pages/SwitchFormPage';
import SwitchPage from './pages/SwitchPage';
import SettingsPage from './pages/SettingsPage';
import DevicesPage from './pages/DevicesPage';
import DiscoveryPage from './pages/DiscoveryPage';
import { Placeholder, SearchPage } from './pages/Placeholders';

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
        <Route path="devices/:id" element={<Placeholder title="Device details" phase="Phase 4" />} />
        <Route path="topology" element={<Placeholder title="Topology" phase="Phase 5" />} />
        <Route path="discovery" element={<DiscoveryPage />} />
        <Route path="settings" element={<SettingsPage />} />
        <Route path="search" element={<SearchPage />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  );
}
