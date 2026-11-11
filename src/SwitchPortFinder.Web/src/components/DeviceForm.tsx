import { useState, type FormEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, CATEGORIES, categoryLabel, type DeviceRow, type ManualDevice } from '../api';
import { ErrorBox, Field } from './Bits';

/**
 * Add a device by hand or edit the manual details of a discovered one: a name, owner, notes and the switch port it is plugged into.
 * A port chosen here is kept across scans and makes the device traceable up to the core switch.
 */
export default function DeviceForm({ device, onDone, onCancel }: { device?: DeviceRow; onDone: (id: number) => void; onCancel: () => void }) {
  const qc = useQueryClient();
  const [mac, setMac] = useState(''); const [name, setName] = useState(device?.pinned || device?.hostname !== 'Unknown Device' ? device?.hostname ?? '' : '');
  const [ip, setIp] = useState(device?.ip ?? ''); const [owner, setOwner] = useState(device?.owner ?? ''); const [notes, setNotes] = useState(device?.notes ?? '');
  const [sw, setSw] = useState<number | ''>(device?.pinned ? device.switchId ?? '' : ''); const [port, setPort] = useState<number | ''>(device?.pinned ? device.portId ?? '' : '');
  const [category, setCategory] = useState<string>(device?.categoryManual ? device.category ?? '' : '');
  const [vlan, setVlan] = useState<string>(device?.pinned && device.vlan ? String(device.vlan) : '');
  const switches = useQuery({ queryKey: ['switches'], queryFn: api.listSwitches });
  const ports = useQuery({ queryKey: ['ports', sw], queryFn: () => api.ports(Number(sw)), enabled: sw !== '' });
  const body = (): ManualDevice => ({ mac: device ? undefined : mac, name, ip: device ? undefined : ip || undefined, owner, notes, category: category || null, switchId: sw === '' ? null : sw, portId: port === '' ? null : port, vlan: vlan ? Number(vlan) : null });
  const save = useMutation({
    mutationFn: () => (device ? api.editDevice(device.id, body()) : api.addDevice(body())),
    onSuccess: (r) => { qc.invalidateQueries({ queryKey: ['devices'] }); qc.invalidateQueries({ queryKey: ['device'] }); qc.invalidateQueries({ queryKey: ['dashboard'] }); onDone(r.device.id); },
  });
  const edge = (ports.data?.ports ?? []).filter((p) => p.type !== 'SwitchUplink');
  return (
    <form className="form modal" onSubmit={(e: FormEvent) => { e.preventDefault(); save.mutate(); }}>
      <h3>{device ? 'Edit device details' : 'Add a device manually'}</h3>
      <p className="muted small">Use this for a device the scans cannot name or place. The name and port you enter are kept when switches are scanned again. Leave the port empty to let scans decide the location.</p>
      <div className="row">
        {!device && <Field label="MAC address" hint="AA:BB:CC:DD:EE:FF, aa-bb-cc-dd-ee-ff or aabb.ccdd.eeff"><input value={mac} onChange={(e) => setMac(e.target.value)} required placeholder="AA:BB:CC:DD:EE:FF" /></Field>}
        <Field label="Name"><input value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. Reception printer" maxLength={255} /></Field>
        {!device && <Field label="IP address (optional)"><input value={ip} onChange={(e) => setIp(e.target.value)} placeholder="IPv4 or IPv6 address" /></Field>}
      </div>
      <div className="row">
        <Field label="Owner / department"><input value={owner} onChange={(e) => setOwner(e.target.value)} maxLength={100} /></Field>
        <Field label="Notes"><input value={notes} onChange={(e) => setNotes(e.target.value)} maxLength={1000} placeholder="asset tag, model, what it does…" /></Field>
      </div>
      <Field label="Type of device" hint={device && !device.categoryManual && device.category ? `Currently detected automatically: ${categoryLabel(device.category)} (${device.categoryReason ?? 'rules'}).` : 'Automatic uses the rules under Settings → Device types.'}>
        <select value={category} onChange={(e) => setCategory(e.target.value)}><option value="">Automatic (use the rules)</option>{CATEGORIES.map((c) => <option key={c} value={c}>{categoryLabel(c)}</option>)}</select>
      </Field>
      <fieldset>
        <legend>Where it is plugged in (makes it traceable)</legend>
        <div className="row">
          <Field label="Switch"><select value={sw} onChange={(e) => { setSw(e.target.value ? Number(e.target.value) : ''); setPort(''); }}><option value="">— not set —</option>{switches.data?.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}</select></Field>
          <Field label="Port"><select value={port} disabled={sw === ''} onChange={(e) => setPort(e.target.value ? Number(e.target.value) : '')} required={sw !== ''}><option value="">{sw === '' ? '—' : ports.isLoading ? 'Loading…' : 'Choose a port'}</option>
            {edge.map((p) => <option key={p.portId} value={p.portId}>{p.name}{p.alias ? ` — ${p.alias}` : ''} ({p.status})</option>)}</select></Field>
          <Field label="VLAN (optional)"><input value={vlan} onChange={(e) => setVlan(e.target.value.replace(/\D/g, ''))} inputMode="numeric" maxLength={4} /></Field>
        </div>
        <span className="muted small">Switch uplink ports are not listed: choose the port the device (or the unmanaged switch it sits behind) is plugged into.</span>
      </fieldset>
      <ErrorBox error={save.error} />
      <div className="buttons"><button className="btn primary" disabled={save.isPending}>{save.isPending ? 'Saving…' : 'Save'}</button><button type="button" className="btn" onClick={onCancel}>Cancel</button></div>
    </form>
  );
}
