import { useNavigate } from 'react-router-dom';
import DeviceForm from '../components/DeviceForm';

export default function AddDevicePage() {
  const nav = useNavigate();
  return <section><div className="toolbar"><h1>Add device</h1></div><DeviceForm onDone={(id) => nav(`/devices/${id}`)} onCancel={() => nav(-1)} /></section>;
}
