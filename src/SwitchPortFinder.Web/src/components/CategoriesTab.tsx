import { useEffect, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, CATEGORIES, categoryLabel, type CategoryRule, type CategoryRuleRequest } from '../api';
import { CategoryBadge, ErrorBox, RowMenu } from './Bits';

const CAPS = [['', '—'], ['WlanAp', 'Wireless access point'], ['Telephone', 'Telephone'], ['Bridge', 'Bridge/switch'], ['Router', 'Router']];
const blank: CategoryRuleRequest = { name: '', category: 'AccessPoint', enabled: true, priority: 50, vendorContains: '', hostnameStartsWith: '', hostnameContains: '', portDescriptionContains: '', neighborContains: '', neighborCapability: '', vlan: null };

const conds = (r: CategoryRule) => [r.vendorContains && `manufacturer contains “${r.vendorContains}”`, r.hostnameStartsWith && `host name starts with “${r.hostnameStartsWith}”`, r.hostnameContains && `host name contains “${r.hostnameContains}”`, r.neighborContains && `neighbor info contains “${r.neighborContains}”`, r.neighborCapability && `neighbor says ${CAPS.find((c) => c[0] === r.neighborCapability)?.[1] ?? r.neighborCapability}`, r.portDescriptionContains && `port description contains “${r.portDescriptionContains}”`, r.vlan && `VLAN ${r.vlan}`].filter(Boolean).join(' · ') || 'No conditions';

function Row({ rule, onChanged }: { rule: CategoryRule; onChanged: () => void }) {
  const [r, setR] = useState(rule); const [edit, setEdit] = useState(false); const dirty = JSON.stringify(r) !== JSON.stringify(rule); useEffect(() => setR(rule), [rule]);
  const t = (k: keyof CategoryRule, label: string, ph = '') => <label className="field"><span>{label}</span><input aria-label={String(k)} placeholder={ph} value={(r[k] as string | null) ?? ''} onChange={(e) => setR({ ...r, [k]: e.target.value })} /></label>;
  const save = useMutation({ mutationFn: () => api.updateCategoryRule(r.id, { ...r, vlan: r.vlan ? Number(r.vlan) : null }), onSuccess: () => { setEdit(false); onChanged(); } });
  const del = useMutation({ mutationFn: () => api.deleteCategoryRule(r.id), onSuccess: onChanged });
  const toggle = useMutation({ mutationFn: (on: boolean) => api.updateCategoryRule(r.id, { ...rule, enabled: on, vlan: rule.vlan ? Number(rule.vlan) : null }), onSuccess: onChanged });
  return (
    <>
      <tr className={rule.enabled ? '' : 'dim'}>
        <td><input type="checkbox" aria-label={`Rule ${rule.name} enabled`} checked={rule.enabled} onChange={(e) => toggle.mutate(e.target.checked)} /></td>
        <td className="num">{rule.priority}</td>
        <td><div className="swname"><strong>{rule.name}</strong></div><div className="muted small">{conds(rule)}</div></td>
        <td><CategoryBadge c={rule.category} /></td>
        <td className="rowactions"><button type="button" className="btn small" onClick={() => setEdit(!edit)} aria-expanded={edit}>{edit ? 'Close' : 'Edit'}</button><RowMenu label={`More actions for ${rule.name}`}>{(close) => <button type="button" role="menuitem" className="danger" onClick={() => { close(); if (confirm(`Delete rule "${rule.name}"?`)) del.mutate(); }}>Delete…</button>}</RowMenu></td>
      </tr>
      {edit && (
        <tr><td colSpan={5} className="roweditor">
          <div className="row">
            <label className="field"><span>Name</span><input aria-label="Name" value={r.name} onChange={(e) => setR({ ...r, name: e.target.value })} /></label>
            <label className="field"><span>Then it is</span><select aria-label="Category" value={r.category} onChange={(e) => setR({ ...r, category: e.target.value })}>{CATEGORIES.map((c) => <option key={c} value={c}>{categoryLabel(c)}</option>)}</select></label>
            <label className="field"><span>Priority (lower first)</span><input aria-label="Priority" type="number" min={1} max={10000} value={r.priority} onChange={(e) => setR({ ...r, priority: Number(e.target.value) })} /></label>
          </div>
          <div className="row">{t('vendorContains', 'Manufacturer contains', 'e.g. Grandstream')}{t('hostnameStartsWith', 'Host name starts with', 'AP-')}{t('neighborContains', 'Neighbor info contains', 'AIR-')}</div>
          <div className="row">
            <label className="field"><span>Neighbor says it is a</span><select aria-label="Neighbor capability" value={r.neighborCapability ?? ''} onChange={(e) => setR({ ...r, neighborCapability: e.target.value })}>{CAPS.map(([v, l]) => <option key={v} value={v}>{l}</option>)}</select></label>
            <label className="field"><span>VLAN</span><input aria-label="VLAN" type="number" min={1} max={4094} value={r.vlan ?? ''} onChange={(e) => setR({ ...r, vlan: e.target.value ? Number(e.target.value) : null })} /></label>
          </div>
          <ErrorBox error={save.error ?? del.error} />
          <div className="buttons"><button className="btn primary" disabled={!dirty || save.isPending} onClick={() => save.mutate()}>Save changes</button><button type="button" className="btn" onClick={() => { setR(rule); setEdit(false); }}>Cancel</button></div>
        </td></tr>
      )}
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
        <thead><tr><th>On</th><th className="num">Priority</th><th>Rule</th><th>Then it is</th><th><span className="sr-only">Actions</span></th></tr></thead>
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
