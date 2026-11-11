import { FormEvent, useEffect, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { api, MANUFACTURERS, type CredentialInput, type Manufacturer, type TestResult } from '../api';
import { ErrorBox, Field, TestResultBox } from '../components/Bits';
import CredentialFields, { emptyCredential } from '../components/CredentialFields';

export default function SwitchFormPage() {
  const { id } = useParams();
  const editId = id ? Number(id) : undefined;
  const nav = useNavigate();
  const [prefill] = useSearchParams();   // "Add this switch" from an unregistered neighbor
  const qc = useQueryClient();
  const existing = useQuery({ queryKey: ['switch', editId], queryFn: () => api.getSwitch(editId!), enabled: !!editId });
  const profiles = useQuery({ queryKey: ['profiles'], queryFn: api.listProfiles });

  const [name, setName] = useState(prefill.get('name') ?? '');
  const [ip, setIp] = useState(prefill.get('ip') ?? '');
  const [manufacturer, setManufacturer] = useState<Manufacturer>('AutoDetect');
  const [other, setOther] = useState('');
  const [model, setModel] = useState('');
  const [location, setLocation] = useState('');
  const [arp, setArp] = useState(false);
  const [wlc, setWlc] = useState(false);
  const [profileId, setProfileId] = useState<number | ''>('');
  const [cred, setCred] = useState<CredentialInput>(emptyCredential());
  const [testResult, setTestResult] = useState<TestResult | null>(null);

  useEffect(() => {
    const s = existing.data;
    if (!s) return;
    setName(s.name); setIp(s.managementIp); setManufacturer(s.manufacturer); setOther(s.manufacturerOther ?? '');
    setModel(s.model ?? ''); setLocation(s.location ?? ''); setArp(s.useAsArpSource); setWlc(!!s.isWirelessController); setProfileId(s.credentialProfileId ?? '');
    if (s.credential) setCred({ ...emptyCredential(), version: s.credential.version, authProtocol: s.credential.authProtocol, privProtocol: s.credential.privProtocol, contextName: s.credential.contextName ?? '' });
  }, [existing.data]);

  const usingProfile = profileId !== '';
  const test = useMutation({
    mutationFn: () => api.testUnsaved({ managementIp: ip, manufacturer, switchId: editId, credentialProfileId: usingProfile ? Number(profileId) : null, credential: usingProfile ? null : cred }),
    onSuccess: setTestResult,
  });
  const save = useMutation({
    mutationFn: () => {
      const body = { name, managementIp: ip, manufacturer, manufacturerOther: other, model, location, useAsArpSource: arp, isWirelessController: wlc,
        credentialProfileId: usingProfile ? Number(profileId) : null, credential: usingProfile ? null : cred };
      return editId ? api.updateSwitch(editId, body) : api.createSwitch(body);
    },
    onSuccess: (s) => { qc.invalidateQueries({ queryKey: ['switches'] }); qc.invalidateQueries({ queryKey: ['switch', s.id] }); nav(`/switches/${s.id}`); },
  });
  const submit = (e: FormEvent) => { e.preventDefault(); save.mutate(); };

  return (
    <section>
      <h1>{editId ? 'Edit Switch' : 'Add Switch'}</h1>
      <form className="form" onSubmit={submit}>
        <div className="row">
          <Field label="Name"><input required value={name} onChange={(e) => setName(e.target.value)} placeholder="CORE-SW-01" /></Field>
          <Field label="Management IP"><input required value={ip} onChange={(e) => { setIp(e.target.value); setTestResult(null); }} placeholder="IPv4 or IPv6" /></Field>
        </div>
        <div className="row">
          <Field label="Manufacturer" hint="Auto-detect uses sysObjectID after the first successful SNMP contact. A manual choice overrides it.">
            <select value={manufacturer} onChange={(e) => setManufacturer(e.target.value as Manufacturer)}>
              {MANUFACTURERS.map((m) => <option key={m.value} value={m.value}>{m.label}</option>)}
            </select>
          </Field>
          {manufacturer === 'OtherGeneric' && <Field label="Manufacturer name"><input value={other} onChange={(e) => setOther(e.target.value)} /></Field>}
          <Field label="Model (optional)"><input value={model} onChange={(e) => setModel(e.target.value)} /></Field>
          <Field label="Location (optional)"><input value={location} onChange={(e) => setLocation(e.target.value)} /></Field>
        </div>

        <fieldset>
          <legend>SNMP credentials</legend>
          <Field label="Credential profile" hint="Pick a saved profile instead of typing credentials.">
            <select value={profileId} onChange={(e) => { setProfileId(e.target.value === '' ? '' : Number(e.target.value)); setTestResult(null); }}>
              <option value="">— enter credentials below —</option>
              {profiles.data?.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
            </select>
          </Field>
          {!usingProfile && <CredentialFields value={cred} onChange={(v) => { setCred(v); setTestResult(null); }} existing={existing.data?.credential} />}
        </fieldset>

        <label className="check">
          <input type="checkbox" checked={arp} onChange={(e) => setArp(e.target.checked)} />
          Use as ARP source <small>(core / Layer 3 switch or router that holds the IP→MAC table; Layer 3 devices are also auto-detected)</small>
        </label>

        <label className="check">
          <input type="checkbox" checked={wlc} onChange={(e) => setWlc(e.target.checked)} />
          This is a Cisco wireless controller <small>(the scan reads its access point list, so the access points appear on the Access points page automatically)</small>
        </label>

        {testResult && <TestResultBox r={testResult} />}
        <ErrorBox error={test.error ?? save.error} />
        <div className="buttons">
          <button type="button" className="btn" disabled={test.isPending || !ip} onClick={() => test.mutate()}>{test.isPending ? 'Testing…' : 'Test Connection'}</button>
          <button type="submit" className="btn primary" disabled={save.isPending}>{save.isPending ? 'Saving…' : 'Save'}</button>
          <button type="button" className="btn" onClick={() => nav(-1)}>Cancel</button>
        </div>
      </form>
    </section>
  );
}
