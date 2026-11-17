import { useNavigate } from 'react-router-dom';
import DeviceForm from '../components/DeviceForm';

import { PageHead } from '../components/Bits';
export default function AddDevicePage() {
  const nav = useNavigate();
  return <section><PageHead icon="M12 5v14M5 12h14" title="Add device" subtitle="Add a device by hand so it can be named and traced" /><DeviceForm onDone={(id) => nav(`/devices/${id}`)} onCancel={() => nav(-1)} /></section>;
}
