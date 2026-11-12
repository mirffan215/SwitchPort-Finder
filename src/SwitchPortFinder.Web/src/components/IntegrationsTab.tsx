import { useEffect, useState, type FormEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, type Integration } from '../api';
import { ErrorBox, Field, ago } from './Bits';

const META: Record<Integration['kind'], { title: string; blurb: string }> = {
  dhcp: { title: 'Windows DHCP server', blurb: 'Reads the lease list (read-only) and uses the host names to name devices that show as "Unknown Device". Needs Windows PowerShell with the DHCP management tools (RSAT) on the portal server, and the portal service account allowed to read the DHCP server.' },
  ad: { title: 'Local domain (Active Directory)', blurb: 'Reads computer accounts over LDAPS with a read-only service account, and adds the operating system and OU to matching devices.' },
  intune: { title: 'Microsoft Intune', blurb: 'Reads managed devices from Microsoft Graph (read-only) and matches them by MAC address to add the device name, user, OS and compliance. Needs the portal server to reach Microsoft online, and an Entra app registration with the read-only application permission DeviceManagementManagedDevices.Read.All (admin consent).' },
};

function Card({ it }: { it: Integration }) {
  const qc = useQueryClient(); const m = META[it.kind];
  const [on, setOn] = useState(it.enabled); const [cfg, setCfg] = useState<Record<string, unknown>>(it.config); const [secret, setSecret] = useState(''); const [saved, setSaved] = useState(false);
  useEffect(() => { setOn(it.enabled); setCfg(it.config); }, [it]);
  const refresh = () => qc.invalidateQueries({ queryKey: ['integrations'] });
  const save = useMutation({ mutationFn: () => api.saveIntegration(it.kind, { enabled: on, config: cfg, secret: secret === '' ? null : secret }), onSuccess: () => { setSecret(''); setSaved(true); refresh(); } });
  const run = useMutation({ mutationFn: () => api.runIntegration(it.kind), onSuccess: () => { refresh(); qc.invalidateQueries({ queryKey: ['devices'] }); } });
  const s = (k: string) => String(cfg[k] ?? ''); const set = (k: string, v: unknown) => { setCfg({ ...cfg, [k]: v }); setSaved(false); };
  return (
    <form className="form panelx" style={{ marginBottom: 16 }} onSubmit={(e: FormEvent) => { e.preventDefault(); save.mutate(); }}>
      <h3>{m.title} {it.enabled ? <span className="badge ok">On</span> : <span className="badge warnb">Off</span>}</h3>
      <p className="hint">{m.blurb}</p>
      <label className="check"><input type="checkbox" checked={on} onChange={(e) => { setOn(e.target.checked); setSaved(false); }} /> Turn this integration on</label>
      {it.kind === 'dhcp' && <Field label="DHCP server (name or IP)"><input value={s('server')} onChange={(e) => set('server', e.target.value)} autoComplete="off" /></Field>}
      {it.kind === 'ad' && <>
        <div className="row">
          <Field label="Domain controller (name or IP)"><input value={s('server')} onChange={(e) => set('server', e.target.value)} autoComplete="off" /></Field>
          <Field label="LDAPS port"><input type="number" min={1} max={65535} value={s('port') || '636'} onChange={(e) => set('port', Number(e.target.value))} /></Field>
        </div>
        <div className="row">
          <Field label="Base DN" hint="Where to look, for example DC=corp,DC=local"><input value={s('baseDn')} onChange={(e) => set('baseDn', e.target.value)} autoComplete="off" /></Field>
          <Field label="Read-only account"><input value={s('bindUser')} onChange={(e) => set('bindUser', e.target.value)} autoComplete="off" placeholder="user@domain" /></Field>
        </div>
        <label className="check"><input type="checkbox" checked={cfg.trustServerCertificate === true} onChange={(e) => set('trustServerCertificate', e.target.checked)} /> Trust the domain controller's certificate even if it is not from a trusted CA <small>(use only if you cannot install your domain CA on this server)</small></label>
      </>}
      {it.kind === 'intune' && <div className="row">
        <Field label="Tenant ID"><input value={s('tenantId')} onChange={(e) => set('tenantId', e.target.value)} autoComplete="off" /></Field>
        <Field label="Application (client) ID"><input value={s('clientId')} onChange={(e) => set('clientId', e.target.value)} autoComplete="off" /></Field>
      </div>}
      {it.kind !== 'dhcp' && <Field label={it.kind === 'ad' ? 'Account password' : 'Client secret'} hint={it.hasSecret ? 'Stored encrypted. Leave empty to keep it.' : undefined}><input type="password" value={secret} onChange={(e) => { setSecret(e.target.value); setSaved(false); }} autoComplete="new-password" placeholder={it.hasSecret ? '•••••••• (unchanged)' : ''} /></Field>}
      <ErrorBox error={save.error ?? run.error} />
      <div className="buttons">
        <button className="btn primary" disabled={save.isPending}>{save.isPending ? 'Saving…' : 'Save'}</button>
        <button type="button" className="btn" disabled={!it.enabled || run.isPending} title={it.enabled ? '' : 'Turn it on and save first'} onClick={() => run.mutate()}>{run.isPending ? 'Reading…' : 'Run now'}</button>
        {saved && <span className="ok">Saved.</span>}
      </div>
      {it.lastRunAt && <p className={it.lastOk ? 'muted small' : 'error small'}>Last run {ago(it.lastRunAt)}: {it.lastMessage}</p>}
    </form>
  );
}

/** Admin · Integrations: optional, all off by default. Read-only; nothing is written to DHCP, the domain or Intune. */
export default function IntegrationsTab() {
  const { data, error } = useQuery({ queryKey: ['integrations'], queryFn: api.integrations });
  return (
    <div>
      <p className="hint">These are optional and switched off. They only fill in what the portal does not know (for example the name of an "Unknown Device"); names and types you set by hand are never overwritten. They run when you click <em>Run now</em>, so they create no regular network traffic.</p>
      <ErrorBox error={error} />
      {data?.map((i) => <Card key={i.kind} it={i} />)}
    </div>
  );
}
