import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { api } from '../api';

export default function Dashboard() {
  const { data } = useQuery({ queryKey: ['switches'], queryFn: api.listSwitches });
  const total = data?.length ?? 0;
  const reachable = data?.filter((s) => s.status === 'Reachable').length ?? 0;
  return (
    <section>
      <h1>Dashboard</h1>
      <div className="cards">
        <div className="card"><div className="num">{total}</div><div>Switches</div></div>
        <div className="card"><div className="num">{reachable}</div><div>Reachable</div></div>
        <div className="card"><div className="num">—</div><div>Devices (after discovery)</div></div>
      </div>
      {total === 0 && (
        <p>No switches yet. <Link to="/switches/new">Add your first switch</Link> or <Link to="/settings">import a list</Link>.</p>
      )}
    </section>
  );
}
