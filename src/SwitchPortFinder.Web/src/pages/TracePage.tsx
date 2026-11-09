import { Fragment } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Link, useParams } from 'react-router-dom';
import { api } from '../api';
import { ConfidenceBadge, ErrorBox } from '../components/Bits';

/** Vertical hop diagram: switch → port → switch → port → device, then the Final Location block. */
export default function TracePage() {
  const id = Number(useParams().id);
  const dev = useQuery({ queryKey: ['device', id], queryFn: () => api.device(id) });
  const { data, error, isLoading } = useQuery({ queryKey: ['trace', id], queryFn: () => api.trace(id) });
  if (isLoading) return <p>Tracing…</p>;
  if (error || !data) return <ErrorBox error={error ?? new Error('Trace failed')} />;
  const d = dev.data?.device;
  return (
    <section>
      <div className="toolbar"><h1>Trace · {d?.hostname ?? `device ${id}`}</h1><div className="spacer" /><Link className="btn" to={`/devices/${id}`}>Device details</Link></div>
      {data.partial && <div className="alert warn" role="status">⚠ {data.message ?? 'Path partially determined'}</div>}
      <div className="trace">
        {data.hops.map((h, i) => (
          <Fragment key={`${h.switchId}-${i}`}>
            <div className="node switchnode"><Link to={`/switches/${h.switchId}`}><strong>{h.switchName}</strong></Link><div className="muted small">{h.switchIp}</div></div>
            <div className="edge">
              <span className="line" />
              <span className="portlabel">{h.portName ? <Link to={`/switches/${h.switchId}${h.ifIndex ? `?port=${h.ifIndex}` : ''}`}>{h.portName}</Link> : <em>port unknown</em>}</span>
              {h.linkConfidence ? <ConfidenceBadge c={h.linkConfidence} /> : null}
              <span className="line" />
            </div>
          </Fragment>
        ))}
        {data.final && <div className="node devnode"><strong>{data.final.hostname}</strong><div className="muted small">{data.final.ip ?? 'IP unknown'} · <span className="mono">{data.final.mac}</span></div></div>}
      </div>
      {data.final && (
        <div className="result final">
          <div className="result-head"><span className="tag">FINAL LOCATION</span></div>
          <dl className="kv cols">
            <dt>Switch</dt><dd><Link to={`/switches/${data.final.switchId}`}>{data.final.switchName}</Link></dd>
            <dt>Port</dt><dd>{data.final.portName}</dd>
            <dt>VLAN</dt><dd>{data.final.vlan ?? '—'}</dd>
            <dt>IP</dt><dd>{data.final.ip ?? 'Unknown'}</dd>
            <dt>MAC</dt><dd className="mono">{data.final.mac}</dd>
            <dt>Hostname</dt><dd>{data.final.hostname}</dd>
          </dl>
          {data.final.lowConfidence && <p className="warn">Lower confidence: this MAC was seen on more than one edge port.</p>}
          <Link className="btn primary" to={`/switches/${data.final.switchId}?port=${data.final.ifIndex}`}>Open Switch</Link>
        </div>
      )}
    </section>
  );
}
