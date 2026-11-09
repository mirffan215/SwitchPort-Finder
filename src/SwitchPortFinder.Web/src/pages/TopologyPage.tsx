import { useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { Background, Controls, Handle, MiniMap, Position, ReactFlow, type Edge, type Node, type NodeProps } from '@xyflow/react';
import '@xyflow/react/dist/style.css';
import { api, type TopoNode } from '../api';
import { ErrorBox } from '../components/Bits';

type SwData = { n: TopoNode };

function SwitchNode({ data }: NodeProps<Node<SwData>>) {
  const n = data.n;
  return (
    <div className={`tnode ${n.external ? 'ext' : ''} ${n.isRoot ? 'root' : ''}`} title={n.external ? 'Not registered: click to add this switch' : 'Click to open this switch'}>
      <Handle type="target" position={Position.Top} /><Handle type="source" position={Position.Bottom} />
      <div className="tname">{!n.external && <span className={`dot ${n.status === 'Reachable' ? 'ok' : n.status === 'Unreachable' ? 'bad' : ''}`} />}{n.name}</div>
      <div className="tsub">{n.ip ?? ''}{n.vendor ? ` · ${n.vendor}` : ''}</div>
      {!n.external && <div className="tsub">{n.ports} ports · {n.uplinks} uplinks · {n.devices} devices</div>}
      {n.external && <div className="tsub">unmanaged · Add this switch</div>}
    </div>
  );
}
const nodeTypes = { sw: SwitchNode };

export default function TopologyPage() {
  const nav = useNavigate();
  const { data, error, isLoading } = useQuery({ queryKey: ['topology'], queryFn: api.topology, refetchInterval: 30_000 });

  const { nodes, edges } = useMemo(() => {
    if (!data) return { nodes: [] as Node<SwData>[], edges: [] as Edge[] };
    const perLevel = new Map<number, number>(); data.nodes.forEach((n) => perLevel.set(n.level, Math.max(perLevel.get(n.level) ?? 0, n.order + 1)));
    const nodes: Node<SwData>[] = data.nodes.map((n) => ({
      id: n.id, type: 'sw', data: { n }, draggable: true,
      position: { x: (n.order - ((perLevel.get(n.level) ?? 1) - 1) / 2) * 280, y: n.level * 170 },
    }));
    const edges: Edge[] = data.edges.map((e) => ({
      id: e.id, source: e.source, target: e.target, label: `${e.sourcePort}  ↔  ${e.targetPort ?? '?'}`, labelBgPadding: [6, 3], labelBgBorderRadius: 4,
      style: e.confidence === 'ConfirmedSwitch' ? { strokeWidth: 2 } : { strokeWidth: 2, strokeDasharray: '6 4', stroke: '#d29922' },
      data: { ...e }, interactionWidth: 24,
    }));
    return { nodes, edges };
  }, [data]);

  if (isLoading) return <p>Loading…</p>;
  return (
    <section>
      <div className="toolbar"><h1>Topology</h1><span className="muted">{data?.nodes.length ?? 0} switches · {data?.edges.length ?? 0} links</span><div className="spacer" />
        <span className="legend"><span className="sw solid" /> Confirmed (LLDP/CDP)</span><span className="legend"><span className="sw dashed" /> Likely (MAC table)</span><span className="legend"><span className="boxe" /> Unmanaged</span></div>
      <ErrorBox error={error} />
      {data && data.nodes.length === 0 && <p>No switches yet. Add switches and scan them; uplinks between them are drawn here.</p>}
      {data && data.nodes.length > 0 && data.edges.length === 0 && <p className="muted">No switch-to-switch links found yet. Links appear after neighbors (LLDP/CDP) or MAC-table evidence are discovered.</p>}
      <div className="topo" data-testid="topology">
        <ReactFlow nodes={nodes} edges={edges} nodeTypes={nodeTypes} fitView fitViewOptions={{ padding: 0.3 }} minZoom={0.2} nodesConnectable={false} proOptions={{ hideAttribution: true }}
          onNodeClick={(_, node) => { const n = (node.data as SwData).n; nav(n.switchId ? `/switches/${n.switchId}` : `/switches/new?name=${encodeURIComponent(n.name)}&ip=${encodeURIComponent(n.ip ?? '')}`); }}
          onEdgeClick={(_, edge) => { const e = edge.data as { sourceSwitchId: number; sourceIfIndex: number }; nav(`/switches/${e.sourceSwitchId}?port=${e.sourceIfIndex}`); }}>
          <Background /><Controls showInteractive={false} /><MiniMap pannable zoomable />
        </ReactFlow>
      </div>
      <p className="muted small">Click a switch to open it; click a link to open its port details.</p>
    </section>
  );
}
