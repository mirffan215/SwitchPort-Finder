export function Placeholder({ title, phase }: { title: string; phase: string }) {
  return (
    <section>
      <h1>{title}</h1>
      <p className="muted">Not built yet. Planned for {phase}. No data is shown here until real discovery results exist.</p>
    </section>
  );
}

