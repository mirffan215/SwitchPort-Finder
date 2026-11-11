import { useEffect, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, CATEGORIES, categoryLabel, type CategoryRule, type CategoryRuleRequest } from '../api';
import { ErrorBox } from './Bits';

const CAPS = [['', '—'], ['WlanAp', 'Wireless access point'], ['Telephone', 'Telephone'], ['Bridge', 'Bridge/switch'], ['Router', 'Router']];
const blank: CategoryRuleRequest = { name: '', category: 'AccessPoint', enabled: true, priority: 50, vendorContains: '', hostnameStartsWith: '', hostnameContains: '', portDescriptionContains: '', neighborContains: '', neighborCapability: '', vlan: null };

function Row({ rule, onChanged }: { rule: CategoryRule; onChanged: () => void }) {
  const [r, setR] = useState(rule); const dirty = JSON.stringify(r) !== JSON.stringify(rule); useEffect(() => setR(rule), [rule]);
  const t = (k: keyof CategoryRule, w = 120, ph = '') => <input aria-label={String(k)} placeholder={ph} value={(r[k] as string | null) ?? ''} onChange={(e) => setR({ ...r, [k]: e.target.value })} style={{ width: w }} />;
  const save = useMutation({ mutationFn: () => api.updateCategoryRule(r.id, { ...r, vlan: r.vlan ? Number(r.vlan) : null }), onSuccess: onChanged });
  const del = useMutation({ mutationFn: () => api.deleteCategoryRule(r.id), onSuccess: onChanged });
  return (
    <>
      <tr>
        <td><input type="checkbox" aria-label="Enabled" checked={r.enabled} onChange={(e) => setR({ ...r, enabled: e.target.checked })} /></td>
        <td><input aria-label="Priority" type="number" min={1} max={10000} value={r.priority} onChange={(e) => setR({ ...r, priority: Number(e.target.value) })} style={{ width: 64 }} /></td>
        <td><input aria-label="Name" value={r.name} onChange={(e) => setR({ ...r, name: e.target.value })} style={{ width: 230 }} /></td>
        <td><select aria-label="Category" value={r.category} onChange={(e) => setR({ ...r, category: e.target.value })}>{CATEGORIES.map((c) => <option key={c} value={c}>{categoryLabel(c)}</option>)}</select></td>
        <td>{t('vendorContains', 110, 'e.g. Grandstream')}</td><td>{t('hostnameStartsWith', 90, 'AP-')}</td><td>{t('neighborContains', 110, 'AIR-')}</td>
        <td><select aria-label="Neighbor capability" value={r.neighborCapability ?? ''} onChange={(e) => setR({ ...r, neighborCapability: e.target.value })}>{CAPS.map(([v, l]) => <option key={v} value={v}>{l}</option>)}</select></td>
        <td><input aria-label="VLAN" type="number" min={1} max={4094} value={r.vlan ?? ''} onChange={(e) => setR({ ...r, vlan: e.target.value ? Number(e.target.value) : null })} style={{ width: 70 }} /></td>
        <td className="actions"><button className="btn small" disabled={!dirty || save.isPending} onClick={() => save.mutate()}>Save</button><button className="link danger" onClick={() => confirm(`Delete rule "${r.name}"?`) && del.mutate()}>Delete</button></td>
      </tr>
      {(save.error || del.error) && <tr><td colSpan={10}><ErrorBox error={save.error ?? del.error} /></td></tr>}
    </>
  );
}

/** Rules that decide whether a device is an access point, an IP phone, ... (all filled-in conditions must match; the lowest priority number wins). */
export default function CategoriesTab() {
  const qc = useQueryClient(); const { data } = useQuery({ queryKey: ['category-rules'], queryFn: api.categoryRules });
  const refresh = () => { qc.invalidateQueries({ queryKey: ['category-rules'] }); qc.invalidateQueries({ queryKey: ['devices'] }); qc.invalidateQueries({ queryKey: ['access-points'] }); };
  const [n, setN] = useState<CategoryRuleRequest>(blank);
  const add = useMutation({ mutationFn: () => api.createCategoryRule({ ...n, vlan: n.vlan ? Number(n.vlan) : null }), onSuccess: () => { setN(blank); refresh(); } });
  const re = useMutation({ mutationFn: api.reapplyCategories, onSuccess: refresh });
  return (
    <div>
      <h3>Device types (access points, IP phones…)</h3>
      <p className="muted small">Each device is given a type by the first matching rule, lowest priority number first. A rule matches when <strong>all</strong> of its filled-in conditions match: manufacturer (from the MAC), host name, what the device announces on its switch port by LLDP/CDP (name, model, “wireless access point” / “telephone” capability) and its VLAN. Cisco access points and phones share the Cisco manufacturer name with everything else Cisco makes, so for them the neighbor information, host name (phones are named SEP + MAC) and VLAN are what count. Changes apply to all devices at once. A type set by hand on a device always wins.</p>
      <div className="tablewrap"><table>
        <thead><tr><th>On</th><th>Priority</th><th>Rule</th><th>Then it is</th><th>Manufacturer contains</th><th>Host name starts with</th><th>Neighbor info contains</th><th>Neighbor says it is a</th><th>VLAN</th><th /></tr></thead>
        <tbody>{data?.map((r) => <Row key={r.id} rule={r} onChanged={refresh} />)}</tbody>
      </table></div>
      <h3 style={{ marginTop: 18 }}>Add a rule</h3>
      <form className="form" onSubmit={(e) => { e.preventDefault(); add.mutate(); }}>
        <div className="row">
          <label className="field"><span>Name</span><input value={n.name} onChange={(e) => setN({ ...n, name: e.target.value })} required placeholder="e.g. Our Ruijie access points" /></label>
          <label className="field"><span>Then it is</span><select value={n.category} onChange={(e) => setN({ ...n, category: e.target.value })}>{CATEGORIES.map((c) => <option key={c} value={c}>{categoryLabel(c)}</option>)}</select></label>
          <label className="field"><span>Priority (lower first)</span><input type="number" min={1} max={10000} value={n.priority} onChange={(e) => setN({ ...n, priority: Number(e.target.value) })} /></label>
        </div>
        <div className="row">
          <label className="field"><span>Manufacturer contains</span><input value={n.vendorContains ?? ''} onChange={(e) => setN({ ...n, vendorContains: e.target.value })} /></label>
          <label className="field"><span>Host name starts with</span><input value={n.hostnameStartsWith ?? ''} onChange={(e) => setN({ ...n, hostnameStartsWith: e.target.value })} /></label>
          <label className="field"><span>Host name contains</span><input value={n.hostnameContains ?? ''} onChange={(e) => setN({ ...n, hostnameContains: e.target.value })} /></label>
          <label className="field"><span>Switch port description contains</span><input value={n.portDescriptionContains ?? ''} onChange={(e) => setN({ ...n, portDescriptionContains: e.target.value })} /></label>
        </div>
        <div className="row">
          <label className="field"><span>Neighbor info contains</span><input value={n.neighborContains ?? ''} onChange={(e) => setN({ ...n, neighborContains: e.target.value })} /></label>
          <label className="field"><span>Neighbor says it is a</span><select value={n.neighborCapability ?? ''} onChange={(e) => setN({ ...n, neighborCapability: e.target.value })}>{CAPS.map(([v, l]) => <option key={v} value={v}>{l}</option>)}</select></label>
          <label className="field"><span>VLAN</span><input type="number" min={1} max={4094} value={n.vlan ?? ''} onChange={(e) => setN({ ...n, vlan: e.target.value ? Number(e.target.value) : null })} /></label>
        </div>
        <ErrorBox error={add.error ?? re.error} />
        {re.data && <div className="alert success">✓ Rules applied: {re.data.changed} device(s) changed type.</div>}
        <div className="buttons"><button className="btn primary" disabled={add.isPending}>Add rule</button><button type="button" className="btn" disabled={re.isPending} onClick={() => re.mutate()}>Re-apply all rules now</button></div>
      </form>
    </div>
  );
}
