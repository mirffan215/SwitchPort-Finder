import { useEffect, useState, type FormEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, type AlertRule, type AlertRuleRequest, type AlertRuleType, type NotificationConfig, type SmtpSecurity } from '../api';
import { ErrorBox, Field, ago } from './Bits';

const TYPES: { v: AlertRuleType; label: string; unit: string; hasLimit: boolean; hint: string }[] = [
  { v: 'UplinkUtilization', label: 'Uplink utilization', unit: '%', hasLimit: true, hint: 'Busier direction of a switch-to-switch link, as % of link speed.' },
  { v: 'PortUtilization', label: 'Any port utilization', unit: '%', hasLimit: true, hint: 'Any connected port.' },
  { v: 'PacketDrops', label: 'Packet drops', unit: 'pkt/s', hasLimit: true, hint: 'Discarded packets per second on a port.' },
  { v: 'PortErrors', label: 'Port errors', unit: 'pkt/s', hasLimit: true, hint: 'Error packets per second on a port.' },
  { v: 'HighCpu', label: 'Switch CPU', unit: '%', hasLimit: true, hint: 'Busiest processor.' },
  { v: 'HighMemory', label: 'Switch memory', unit: '%', hasLimit: true, hint: 'Memory in use.' },
  { v: 'HighLatency', label: 'SNMP latency', unit: 'ms', hasLimit: true, hint: 'Round-trip time of the SNMP request.' },
  { v: 'SwitchUnreachable', label: 'Switch unreachable', unit: '', hasLimit: false, hint: 'No SNMP answer for the number of minutes given.' },
  { v: 'SwitchRestarted', label: 'Switch restarted', unit: '', hasLimit: false, hint: 'Uptime dropped (reboot) within the last number of minutes given.' },
];
const typeOf = (t: AlertRuleType) => TYPES.find((x) => x.v === t)!;
const PORTS: Record<SmtpSecurity, number> = { StartTls: 587, SslOnConnect: 465, None: 25 };

function MailServer() {
  const qc = useQueryClient();
  const { data } = useQuery({ queryKey: ['notif-config'], queryFn: api.notifConfig });
  const [f, setF] = useState<NotificationConfig | null>(null); const [rcpt, setRcpt] = useState(''); const [pw, setPw] = useState(''); const [clearPw, setClearPw] = useState(false);
  const [testTo, setTestTo] = useState(''); const [saved, setSaved] = useState(false);
  useEffect(() => { if (data) { setF(data); setRcpt(data.recipients.join('\n')); } }, [data]);
  const save = useMutation({
    mutationFn: () => api.saveNotifConfig({ ...f!, password: clearPw ? '' : pw === '' ? null : pw, recipients: rcpt.split(/[\n;,]+/).map((x) => x.trim()).filter(Boolean) }),
    onSuccess: (r) => { setF(r); setPw(''); setClearPw(false); setSaved(true); qc.invalidateQueries({ queryKey: ['notif-config'] }); },
  });
  const test = useMutation({ mutationFn: () => api.testMail(testTo), onSuccess: () => qc.invalidateQueries({ queryKey: ['mail-log'] }) });
  if (!f) return <p>Loading…</p>;
  const set = (p: Partial<NotificationConfig>) => { setF({ ...f, ...p }); setSaved(false); };
  return (
    <form className="form" onSubmit={(e: FormEvent) => { e.preventDefault(); save.mutate(); }}>
      <h3>E-mail server</h3>
      <label className="check"><input type="checkbox" checked={f.enabled} onChange={(e) => set({ enabled: e.target.checked })} /> Send e-mail notifications</label>
      <div className="row">
        <Field label="Server (host name)"><input value={f.host} onChange={(e) => set({ host: e.target.value })} placeholder="smtp.example.com" autoComplete="off" /></Field>
        <Field label="Port"><input type="number" min={1} max={65535} value={f.port} onChange={(e) => set({ port: Number(e.target.value) })} /></Field>
        <Field label="Connection security" hint="STARTTLS (587) or SSL/TLS (465) use TLS 1.2/1.3 and verify the server certificate.">
          <select value={f.security} onChange={(e) => { const s = e.target.value as SmtpSecurity; set({ security: s, port: PORTS[s] }); }}>
            <option value="StartTls">STARTTLS (recommended, port 587)</option><option value="SslOnConnect">SSL/TLS (port 465)</option><option value="None">None (internal relay without sign-in, port 25)</option>
          </select>
        </Field>
      </div>
      <div className="row">
        <Field label="User name (optional)"><input value={f.username ?? ''} onChange={(e) => set({ username: e.target.value })} autoComplete="off" disabled={f.security === 'None'} /></Field>
        <Field label="Password" hint={f.hasPassword ? 'A password is stored (encrypted). Leave empty to keep it.' : undefined}><input type="password" value={pw} onChange={(e) => { setPw(e.target.value); setSaved(false); }} autoComplete="new-password" disabled={f.security === 'None' || clearPw} placeholder={f.hasPassword ? '•••••••• (unchanged)' : ''} /></Field>
        {f.hasPassword && <label className="check align-end"><input type="checkbox" checked={clearPw} onChange={(e) => setClearPw(e.target.checked)} /> Remove the stored password</label>}
      </div>
      {f.security === 'None' && <div className="alert warn">Without TLS the message and any alert content travel in clear text, and a user name/password cannot be used. Use only for an internal relay restricted by IP address.</div>}
      <div className="row">
        <Field label="From address"><input type="email" value={f.fromAddress} onChange={(e) => set({ fromAddress: e.target.value })} placeholder="switchport@example.com" /></Field>
        <Field label="From name"><input value={f.fromName} onChange={(e) => set({ fromName: e.target.value })} /></Field>
        <Field label="Portal address for links (optional)" hint="Full address including the scheme and port, as users type it in the browser."><input value={f.publicBaseUrl ?? ''} onChange={(e) => set({ publicBaseUrl: e.target.value })} placeholder="address of this portal" /></Field>
      </div>
      <Field label="Recipients" hint="One address per line (up to 20)."><textarea rows={3} value={rcpt} onChange={(e) => { setRcpt(e.target.value); setSaved(false); }} /></Field>
      <label className="check"><input type="checkbox" checked={f.trustServerCertificate} onChange={(e) => set({ trustServerCertificate: e.target.checked })} /> Trust the server certificate even if it is self-signed or from an unknown CA <small>(only for an internal relay you control)</small></label>
      <ErrorBox error={save.error} />
      {saved && <div className="alert success">✓ Saved.</div>}
      <div className="buttons"><button className="btn primary" disabled={save.isPending}>{save.isPending ? 'Saving…' : 'Save'}</button></div>
      <fieldset><legend>Send a test message</legend>
        <div className="row"><Field label="To (empty = the recipients above)"><input type="email" value={testTo} onChange={(e) => setTestTo(e.target.value)} /></Field>
          <button type="button" className="btn align-end" disabled={test.isPending} onClick={() => test.mutate()}>{test.isPending ? 'Sending…' : 'Send test'}</button></div>
        <span className="muted small">Test with the saved settings: save first if you changed anything.</span>
        {test.data && <div className={`alert ${test.data.success ? 'success' : 'error'}`} role="status">{test.data.success ? '✓ ' : '✗ '}{test.data.message}</div>}
        <ErrorBox error={test.error} />
      </fieldset>
    </form>
  );
}

function RuleRow({ rule, switches, onChanged }: { rule: AlertRule; switches: { id: number; name: string }[]; onChanged: () => void }) {
  const [r, setR] = useState(rule); const t = typeOf(r.type); const dirty = JSON.stringify(r) !== JSON.stringify(rule);
  useEffect(() => setR(rule), [rule]);
  const body = (): AlertRuleRequest => ({ name: r.name, type: r.type, enabled: r.enabled, threshold: r.threshold, durationMinutes: r.durationMinutes, switchId: r.switchId ?? null, cooldownMinutes: r.cooldownMinutes, notifyOnResolve: r.notifyOnResolve });
  const save = useMutation({ mutationFn: () => api.updateRule(r.id, body()), onSuccess: onChanged });
  const del = useMutation({ mutationFn: () => api.deleteRule(r.id), onSuccess: onChanged });
  return (
    <>
      <tr>
        <td><input type="checkbox" aria-label="Enabled" checked={r.enabled} onChange={(e) => setR({ ...r, enabled: e.target.checked })} /></td>
        <td><input aria-label="Name" value={r.name} onChange={(e) => setR({ ...r, name: e.target.value })} style={{ width: 210 }} /><div className="muted small">{t.label} — {t.hint}</div></td>
        <td>{t.hasLimit ? <span style={{ whiteSpace: 'nowrap' }}><input aria-label="Limit" type="number" min={0.1} step="any" value={r.threshold} onChange={(e) => setR({ ...r, threshold: Number(e.target.value) })} style={{ width: 78 }} /> {t.unit}</span> : '—'}</td>
        <td><input aria-label="Minutes" type="number" min={0} max={1440} value={r.durationMinutes} onChange={(e) => setR({ ...r, durationMinutes: Number(e.target.value) })} style={{ width: 70 }} /> min</td>
        <td><select aria-label="Switch" value={r.switchId ?? ''} onChange={(e) => setR({ ...r, switchId: e.target.value ? Number(e.target.value) : null })}><option value="">All switches</option>{switches.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}</select></td>
        <td><input aria-label="Remind every" type="number" min={0} max={10080} value={r.cooldownMinutes} onChange={(e) => setR({ ...r, cooldownMinutes: Number(e.target.value) })} style={{ width: 76 }} /> min</td>
        <td><input type="checkbox" aria-label="Notify when resolved" checked={r.notifyOnResolve} onChange={(e) => setR({ ...r, notifyOnResolve: e.target.checked })} /></td>
        <td className="actions"><button className="btn small" disabled={!dirty || save.isPending} onClick={() => save.mutate()}>Save</button><button className="link danger" onClick={() => confirm(`Delete rule "${r.name}"?`) && del.mutate()}>Delete</button></td>
      </tr>
      {(save.error || del.error) && <tr><td colSpan={8}><ErrorBox error={save.error ?? del.error} /></td></tr>}
    </>
  );
}

function Rules() {
  const qc = useQueryClient();
  const { data } = useQuery({ queryKey: ['alert-rules'], queryFn: api.alertRules }); const switches = useQuery({ queryKey: ['switches'], queryFn: api.listSwitches });
  const refresh = () => qc.invalidateQueries({ queryKey: ['alert-rules'] });
  const [nt, setNt] = useState<AlertRuleType>('UplinkUtilization');
  const add = useMutation({ mutationFn: () => { const t = typeOf(nt); return api.createRule({ name: `${t.label}${t.hasLimit ? ' above ' + (t.unit === '%' ? 80 : t.unit === 'ms' ? 200 : 5) + t.unit : ''}`, type: nt, enabled: true, threshold: t.hasLimit ? (t.unit === '%' ? 80 : t.unit === 'ms' ? 200 : 5) : 0, durationMinutes: 10, switchId: null, cooldownMinutes: 240, notifyOnResolve: true }); }, onSuccess: refresh });
  return (
    <div>
      <h3>Alert rules</h3>
      <p className="muted small">An alert opens when the reading stays at or above the limit for the given minutes, and closes when it falls below 90% of the limit (so it does not flap). One e-mail is sent per check cycle for everything that is new, still open after the reminder interval, or resolved. Readings come from the health poll, so make sure it is on (Settings → General → Health poll interval).</p>
      <div className="tablewrap"><table>
        <thead><tr><th>On</th><th>Rule</th><th>Limit</th><th>For</th><th>Applies to</th><th>Remind every</th><th>Tell me when resolved</th><th /></tr></thead>
        <tbody>{data?.map((r) => <RuleRow key={r.id} rule={r} switches={switches.data ?? []} onChanged={refresh} />)}{data?.length === 0 && <tr><td colSpan={8} className="muted">No rules yet.</td></tr>}</tbody>
      </table></div>
      <div className="toolbar" style={{ marginTop: 10 }}><select aria-label="New rule type" value={nt} onChange={(e) => setNt(e.target.value as AlertRuleType)}>{TYPES.map((t) => <option key={t.v} value={t.v}>{t.label}</option>)}</select><button className="btn" disabled={add.isPending} onClick={() => add.mutate()}>+ Add rule</button></div>
      <ErrorBox error={add.error} />
    </div>
  );
}

function History() {
  const alerts = useQuery({ queryKey: ['alerts'], queryFn: api.alerts, refetchInterval: 30_000 }); const log = useQuery({ queryKey: ['mail-log'], queryFn: api.mailLog });
  return (
    <div>
      <h3>Active alerts {alerts.data && <span className={`badge ${alerts.data.activeCount ? 'bad' : 'ok'}`}>{alerts.data.activeCount}</span>}</h3>
      <div className="tablewrap tall"><table>
        <thead><tr><th>Alert</th><th>Since</th><th>Notified</th></tr></thead>
        <tbody>{alerts.data?.active.map((a) => <tr key={a.id}><td><strong>{a.title}</strong><div className="muted small">{a.detail}</div></td><td>{ago(a.firstAt)}</td><td>{a.notifyCount}×</td></tr>)}{alerts.data?.active.length === 0 && <tr><td colSpan={3} className="muted">Nothing is alerting.</td></tr>}</tbody></table></div>
      <h3 style={{ marginTop: 16 }}>Recently resolved</h3>
      <div className="tablewrap tall"><table><thead><tr><th>Alert</th><th>Opened</th><th>Resolved</th></tr></thead>
        <tbody>{alerts.data?.recent.map((a) => <tr key={a.id}><td>{a.title}</td><td>{ago(a.firstAt)}</td><td>{ago(a.resolvedAt)}</td></tr>)}{alerts.data?.recent.length === 0 && <tr><td colSpan={3} className="muted">None yet.</td></tr>}</tbody></table></div>
      <h3 style={{ marginTop: 16 }}>E-mail log</h3>
      <div className="tablewrap tall"><table><thead><tr><th>When</th><th>Subject</th><th>To</th><th>Result</th></tr></thead>
        <tbody>{log.data?.map((l, i) => <tr key={i}><td>{ago(l.at)}</td><td>{l.subject}</td><td className="small">{l.recipients}</td><td>{l.success ? <span className="badge ok">Sent</span> : <span className="badge bad" title={l.error ?? ''}>Failed</span>}{!l.success && <div className="muted small">{l.error}</div>}</td></tr>)}{log.data?.length === 0 && <tr><td colSpan={4} className="muted">Nothing sent yet.</td></tr>}</tbody></table></div>
    </div>
  );
}

export default function NotificationsTab() { return <div style={{ display: 'grid', gap: 28 }}><MailServer /><Rules /><History /></div>; }
