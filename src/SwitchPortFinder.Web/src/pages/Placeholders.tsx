import { useSearchParams } from 'react-router-dom';

export function Placeholder({ title, phase }: { title: string; phase: string }) {
  return (
    <section>
      <h1>{title}</h1>
      <p className="muted">Not built yet. Planned for {phase}. No data is shown here until real discovery results exist.</p>
    </section>
  );
}

export function SearchPage() {
  const [p] = useSearchParams();
  return (
    <section>
      <h1>Search</h1>
      <p>Query: <code>{p.get('q')}</code></p>
      <p className="muted">Global search arrives in Phase 4, after discovery (Phase 2) has collected devices to search.</p>
    </section>
  );
}
