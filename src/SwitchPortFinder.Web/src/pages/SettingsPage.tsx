import { useEffect, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { api, type CredentialInput, type ImportCommit, type ImportPreview, type ProfileDto, type RuntimeSettings, type UserRole } from '../api';
import MfaSetup, { RecoveryCodes } from '../components/MfaSetup';
import NotificationsTab from '../components/NotificationsTab';
import CategoriesTab from '../components/CategoriesTab';
import CompanyTab from '../components/CompanyTab';
import IntegrationsTab from '../components/IntegrationsTab';
import { useAuth } from '../components/Auth';
import { ErrorBox, Field } from '../components/Bits';
import CredentialFields, { emptyCredential } from '../components/CredentialFields';

type Tab = 'profiles' | 'import' | 'general' | 'notifications' | 'company' | 'integrations' | 'types' | 'users' | 'account';

const Ico = ({ d }: { d: string }) => <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d={d} /></svg>;
interface Item { id: Tab; label: string; desc: string; icon: string; admin?: boolean; auth?: boolean }
const GROUPS: { title: string; items: Item[] }[] = [
  { title: 'Network', items: [
    { id: 'import', label: 'Import switches', desc: 'Add many switches at once from a CSV file, or export the current list.', icon: 'M12 3v12M7 10l5 5 5-5M4 21h16', admin: true },
    { id: 'profiles', label: 'SNMP credential profiles', desc: 'Reusable SNMP communities and SNMPv3 users, so you enter them once and apply them to many switches.', icon: 'M15 7a4 4 0 1 1-3.9 4.9L4 19v-3H1v-3h4l6.1-6.1A4 4 0 0 1 15 7z', admin: true },
    { id: 'types', label: 'Device types', desc: 'Rules that decide whether a device is an access point, phone, printer, camera or server.', icon: 'M4 6h16M4 12h16M4 18h10', admin: true },
    { id: 'integrations', label: 'Integrations', desc: 'Optional DHCP, domain and Intune lookups that name unknown devices. All off by default.', icon: 'M10 13a5 5 0 0 0 7 0l3-3a5 5 0 0 0-7-7l-1 1M14 11a5 5 0 0 0-7 0l-3 3a5 5 0 0 0 7 7l1-1', admin: true },
  ] },
  { title: 'System', items: [
    { id: 'general', label: 'Scanning & status', desc: 'SNMP timeouts, scan options and when a device is shown as offline or stale.', icon: 'M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6zM19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1-2 3.4-.2-.1a1.7 1.7 0 0 0-1.9.2 1.7 1.7 0 0 0-1 1.5V22h-4v-.1a1.7 1.7 0 0 0-1-1.5 1.7 1.7 0 0 0-1.9-.2l-.2.1-2-3.4.1-.1A1.7 1.7 0 0 0 4.6 15a1.7 1.7 0 0 0-1.5-1H3v-4h.1A1.7 1.7 0 0 0 4.6 9a1.7 1.7 0 0 0-.3-1.8L4.2 7l2-3.4.2.1a1.7 1.7 0 0 0 1.9-.2A1.7 1.7 0 0 0 9.3 2H13.3a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.9.2l.2-.1 2 3.4-.1.1A1.7 1.7 0 0 0 19.4 9a1.7 1.7 0 0 0 1.5 1h.1v4h-.1a1.7 1.7 0 0 0-1.5 1z', admin: true },
    { id: 'notifications', label: 'E-mail & alerts', desc: 'Mail server (TLS) and alert rules such as uplink utilization or a switch going down.', icon: 'M4 6h16v12H4zM4 7l8 6 8-6', admin: true },
    { id: 'company', label: 'Company & logo', desc: 'Your company name, website and logo, shown in the menu and on the sign-in page.', icon: 'M3 21V8l9-5 9 5v13M9 21v-6h6v6', admin: true },
  ] },
  { title: 'Access', items: [
    { id: 'users', label: 'Users', desc: 'Portal accounts and roles (administrator or read-only viewer).', icon: 'M16 11a4 4 0 1 0-8 0M4 21a8 8 0 0 1 16 0', admin: true, auth: true },
    { id: 'account', label: 'My account', desc: 'Change your password and manage two-factor authentication.', icon: 'M12 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8zM5 21a7 7 0 0 1 14 0', auth: true },
  ] },
];

export default function SettingsPage() {
  const { me, canWrite } = useAuth();
  const [tab, setTab] = useState<Tab>(canWrite ? 'import' : 'account');
  const groups = GROUPS.map((g) => ({ ...g, items: g.items.filter((i) => (!i.admin || canWrite) && (!i.auth || me.authRequired)) })).filter((g) => g.items.length > 0);
  const current = groups.flatMap((g) => g.items).find((i) => i.id === tab);
  return (
    <section className="settings">
      <h1 className="sr-only">Settings</h1>
      <nav className="setnav" aria-label="Settings sections">
        {groups.map((g) => (
          <div key={g.title} className="setgroup">
            <div className="setgroup-title">{g.title}</div>
            {g.items.map((i) => <button key={i.id} type="button" className={tab === i.id ? 'active' : ''} aria-current={tab === i.id ? 'page' : undefined} onClick={() => setTab(i.id)}><Ico d={i.icon} /><span>{i.label}</span></button>)}
          </div>
        ))}
      </nav>
      <div className="setmain">
        {current && <header className="sethead"><h2>{current.label}</h2><p>{current.desc}</p></header>}
        <div className="setbody">
      {!canWrite && !me.authRequired && <p>Not available.</p>}
      {canWrite && tab === 'import' && <ImportTab />}
      {canWrite && tab === 'profiles' && <ProfilesTab />}
      {canWrite && tab === 'general' && <GeneralTab />}
      {canWrite && tab === 'notifications' && <NotificationsTab />}
      {canWrite && tab === 'integrations' && <IntegrationsTab />}
      {canWrite && tab === 'company' && <CompanyTab />}
      {canWrite && tab === 'types' && <CategoriesTab />}
      {canWrite && tab === 'users' && <UsersTab />}
      {tab === 'account' && <AccountTab />}
        </div>
      </div>
    </section>
  );
}

function TwoFactor() {
  const { me } = useAuth(); const qc = useQueryClient();
  const [mode, setMode] = useState<'idle' | 'enrol' | 'disable' | 'codes'>('idle');
  const [pw, setPw] = useState(''); const [code, setCode] = useState(''); const [codes, setCodes] = useState<string[] | null>(null);
  const off = useMutation({ mutationFn: () => api.mfaDisable(pw, code), onSuccess: () => { setMode('idle'); setPw(''); setCode(''); qc.invalidateQueries({ queryKey: ['me'] }); } });
  const regen = useMutation({ mutationFn: () => api.mfaRecoveryCodes(pw, code), onSuccess: (r) => { setCodes(r.recoveryCodes); setPw(''); setCode(''); } });
  const required = me.role === 'Admin';
  return (
    <div className="form" style={{ marginTop: 24 }}>
      <h3>Two-factor authentication {me.mfaEnabled ? <span className="badge ok">On</span> : <span className="badge warnb">Off</span>}</h3>
      {codes ? <RecoveryCodes codes={codes} onDone={() => { setCodes(null); setMode('idle'); }} /> : mode === 'enrol' ? <MfaSetup onFinished={() => setMode('idle')} /> : mode === 'idle' ? (
        <>
          <p className="muted">{me.mfaEnabled ? 'Your account asks for a code from your authenticator app at every sign-in.' : 'Add a second step to your sign-in with an authenticator app.'}{required && ' Required for administrators.'}</p>
          <div className="buttons">
            {!me.mfaEnabled && <button className="btn primary" onClick={() => setMode('enrol')}>Set up authenticator app</button>}
            {me.mfaEnabled && <button className="btn" onClick={() => setMode('codes')}>New recovery codes</button>}
            {me.mfaEnabled && !required && <button className="btn" onClick={() => setMode('disable')}>Turn off</button>}
          </div>
        </>
      ) : (
        <form className="form" onSubmit={(e) => { e.preventDefault(); (mode === 'disable' ? off : regen).mutate(); }}>
          <p className="muted">{mode === 'disable' ? 'Confirm with your password and a current code to turn two-factor off.' : 'Confirm with your password and a current code. The old recovery codes stop working.'}</p>
          <Field label="Password"><input type="password" autoComplete="current-password" value={pw} onChange={(e) => setPw(e.target.value)} required /></Field>
          <Field label="6-digit code"><input inputMode="numeric" autoComplete="one-time-code" value={code} onChange={(e) => setCode(e.target.value)} required /></Field>
          <ErrorBox error={off.error ?? regen.error} />
          <div className="buttons"><button className="btn primary" disabled={off.isPending || regen.isPending}>{mode === 'disable' ? 'Turn off' : 'Generate new codes'}</button><button type="button" className="btn" onClick={() => setMode('idle')}>Cancel</button></div>
        </form>
      )}
    </div>
  );
}

function AccountTab() {
  const [cur, setCur] = useState(''); const [pw, setPw] = useState(''); const [pw2, setPw2] = useState(''); const [done, setDone] = useState(false);
  const change = useMutation({ mutationFn: () => api.changePassword(cur, pw), onSuccess: () => { setDone(true); setCur(''); setPw(''); setPw2(''); } });
  return (
    <>
    <form className="form" onSubmit={(e) => { e.preventDefault(); setDone(false); change.mutate(); }}>
      <h3>Change password</h3>
      <Field label="Current password"><input type="password" autoComplete="current-password" value={cur} onChange={(e) => setCur(e.target.value)} required /></Field>
      <Field label="New password" hint="At least 12 characters; must not contain your username."><input type="password" autoComplete="new-password" value={pw} onChange={(e) => setPw(e.target.value)} required /></Field>
      <Field label="Repeat new password"><input type="password" autoComplete="new-password" value={pw2} onChange={(e) => setPw2(e.target.value)} required /></Field>
      {pw2 && pw !== pw2 && <div className="alert error">The new passwords do not match.</div>}
      <ErrorBox error={change.error} />
      {done && <div className="alert success">✓ Password changed.</div>}
      <div className="buttons"><button className="btn primary" disabled={change.isPending || !cur || !pw || pw !== pw2}>Change password</button></div>
    </form>
    <TwoFactor />
    </>
  );
}

function UsersTab() {
  const qc = useQueryClient(); const { me } = useAuth();
  const { data, error } = useQuery({ queryKey: ['users'], queryFn: api.users });
  const refresh = () => qc.invalidateQueries({ queryKey: ['users'] });
  const [name, setName] = useState(''); const [pw, setPw] = useState(''); const [role, setRole] = useState<UserRole>('Viewer');
  const create = useMutation({ mutationFn: () => api.createUser({ username: name, password: pw, role }), onSuccess: () => { setName(''); setPw(''); refresh(); } });
  const update = useMutation({ mutationFn: (a: { id: number; body: { role?: UserRole; disabled?: boolean; newPassword?: string; resetMfa?: boolean } }) => api.updateUser(a.id, a.body), onSuccess: refresh });
  const del = useMutation({ mutationFn: api.deleteUser, onSuccess: refresh });
  return (
    <div>
      <p className="muted"><strong>Admin</strong> can change anything. <strong>Viewer</strong> can search, browse, trace and export, but cannot add, edit, scan or delete. Passwords are stored only as salted hashes. An account is locked for a while after repeated wrong passwords.</p>
      <ErrorBox error={error ?? update.error ?? del.error} />
      <div className="tablewrap"><table>
        <thead><tr><th>User</th><th>Role</th><th>Status</th><th>2FA</th><th>Last sign-in</th><th /></tr></thead>
        <tbody>{data?.map((u) => (
          <tr key={u.id}>
            <td>{u.username}{u.username.toLowerCase() === me.username?.toLowerCase() && <span className="tag">you</span>}</td>
            <td><select value={u.role} onChange={(e) => update.mutate({ id: u.id, body: { role: e.target.value as UserRole } })}><option>Viewer</option><option>Admin</option></select></td>
            <td>{u.disabled ? <span className="badge bad">Disabled</span> : u.locked ? <span className="badge warnb">Locked</span> : <span className="badge ok">Active</span>}</td>
            <td>{u.mfaEnabled ? <span className="badge ok">On</span> : <span className="badge warnb">Off</span>}</td>
            <td>{u.lastLoginAt ? new Date(u.lastLoginAt).toLocaleString() : '—'}</td>
            <td className="actions">
              {u.mfaEnabled && <button className="link" onClick={() => confirm(`Reset two-factor for ${u.username}? They will have to set up an authenticator app again.`) && update.mutate({ id: u.id, body: { resetMfa: true } })}>Reset 2FA</button>}
              <button className="link" onClick={() => update.mutate({ id: u.id, body: { disabled: !u.disabled } })}>{u.disabled ? 'Enable' : 'Disable'}</button>
              <button className="link" onClick={() => { const p = prompt(`New password for ${u.username} (12+ characters):`); if (p) update.mutate({ id: u.id, body: { newPassword: p } }); }}>Reset password</button>
              <button className="link danger" onClick={() => confirm(`Delete ${u.username}?`) && del.mutate(u.id)}>Delete</button>
            </td>
          </tr>))}</tbody>
      </table></div>
      <form className="form modal" onSubmit={(e) => { e.preventDefault(); create.mutate(); }}>
        <h3>Add user</h3>
        <div className="row">
          <Field label="Username"><input value={name} onChange={(e) => setName(e.target.value)} required autoComplete="off" /></Field>
          <Field label="Password" hint="At least 12 characters."><input type="password" value={pw} onChange={(e) => setPw(e.target.value)} required autoComplete="new-password" /></Field>
          <Field label="Role"><select value={role} onChange={(e) => setRole(e.target.value as UserRole)}><option>Viewer</option><option>Admin</option></select></Field>
        </div>
        <ErrorBox error={create.error} />
        <div className="buttons"><button className="btn primary" disabled={create.isPending}>Add user</button></div>
      </form>
    </div>
  );
}

function SecureExport() {
  const { me } = useAuth();
  const [open, setOpen] = useState(false); const [pw, setPw] = useState(''); const [pass, setPass] = useState(''); const [pass2, setPass2] = useState(''); const [ok, setOk] = useState(false);
  const exp = useMutation({
    mutationFn: () => api.exportSecure(pw, pass),
    onSuccess: ({ blob, name }) => { const a = document.createElement('a'); a.href = URL.createObjectURL(blob); a.download = name; a.click(); URL.revokeObjectURL(a.href); setOk(true); setPw(''); setPass(''); setPass2(''); },
  });
  if (!open) return <p><button className="btn" onClick={() => { setOpen(true); setOk(false); }}>Export switches with credentials (encrypted)…</button></p>;
  return (
    <form className="form modal" onSubmit={(e) => { e.preventDefault(); setOk(false); exp.mutate(); }}>
      <h3>Export switches with credentials</h3>
      <p className="muted small">The file is encrypted (AES-256) with the passphrase you choose and can only be opened by importing it here. Without the passphrase it is unreadable. Keep the passphrase separate from the file, and delete the file when you are done. This action is written to the application log.</p>
      {me.authRequired && <Field label="Your password"><input type="password" autoComplete="current-password" value={pw} onChange={(e) => setPw(e.target.value)} required /></Field>}
      <div className="row">
        <Field label="File passphrase" hint="At least 12 characters. Not your login password."><input type="password" autoComplete="new-password" value={pass} onChange={(e) => setPass(e.target.value)} required minLength={12} /></Field>
        <Field label="Repeat passphrase"><input type="password" autoComplete="new-password" value={pass2} onChange={(e) => setPass2(e.target.value)} required /></Field>
      </div>
      {pass2 && pass !== pass2 && <div className="alert error">The passphrases do not match.</div>}
      <ErrorBox error={exp.error} />
      {ok && <div className="alert success">✓ Downloaded. To restore, choose this .spfenc file under “File” below and enter the passphrase.</div>}
      <div className="buttons"><button className="btn primary" disabled={exp.isPending || pass.length < 12 || pass !== pass2}>{exp.isPending ? 'Encrypting…' : 'Export encrypted file'}</button><button type="button" className="btn" onClick={() => setOpen(false)}>Close</button></div>
    </form>
  );
}

function ImportTab() {
  const qc = useQueryClient();
  const [passphrase, setPassphrase] = useState('');
  const [file, setFile] = useState<File | null>(null);
  const [mode, setMode] = useState<'SkipExisting' | 'UpdateExisting'>('SkipExisting');
  const [preview, setPreview] = useState<ImportPreview | null>(null);
  const [done, setDone] = useState<ImportCommit | null>(null);
  const prev = useMutation({ mutationFn: () => api.previewImport(file!, mode, passphrase), onSuccess: (p) => { setPreview(p); setDone(null); } });
  const commit = useMutation({
    mutationFn: () => api.commitImport(preview!.previewId, mode),
    onSuccess: (r) => { setDone(r); setPreview(null); setFile(null); qc.invalidateQueries({ queryKey: ['switches'] }); },
  });
  const ok = preview ? preview.new + preview.update : 0;
  return (
    <div>
      <div className="alert warn">⚠ Import files may contain community strings or passwords. They are processed in memory and are not kept on the server — delete your copy after importing. Prefer credential profiles so secrets are not in the file.</div>
      <p>
        Template: <a href="/api/admin/import/template?format=csv">CSV</a> · <a href="/api/admin/import/template?format=xlsx">Excel</a>
        {' · '}Export current switches (no credentials): <a href="/api/admin/export/switches?format=csv">CSV</a> · <a href="/api/admin/export/switches?format=xlsx">Excel</a>
      </p>
      <SecureExport />
      <div className="row">
        <Field label="File (.csv, .xlsx or encrypted .spfenc)"><input type="file" accept=".csv,.xlsx,.spfenc" onChange={(e) => { setFile(e.target.files?.[0] ?? null); setPreview(null); setDone(null); }} /></Field>
        {file?.name.toLowerCase().endsWith('.spfenc') && <Field label="File passphrase"><input type="password" autoComplete="off" value={passphrase} onChange={(e) => setPassphrase(e.target.value)} /></Field>}
        <Field label="If a switch with the same Management IP exists">
          <select value={mode} onChange={(e) => { setMode(e.target.value as typeof mode); setPreview(null); }}>
            <option value="SkipExisting">Skip existing</option><option value="UpdateExisting">Update existing</option>
          </select>
        </Field>
        <button className="btn primary align-end" disabled={!file || prev.isPending} onClick={() => prev.mutate()}>{prev.isPending ? 'Reading…' : 'Preview'}</button>
      </div>
      <ErrorBox error={prev.error ?? commit.error} />
      {preview && (
        <>
          <p><strong>{preview.fileName}</strong>: {preview.total} rows — <span className="badge ok">{preview.new} new</span> <span className="badge info">{preview.update} update</span> <span className="badge muted">{preview.skipped} skipped</span> <span className="badge bad">{preview.errors} errors</span>. Nothing is saved until you commit.</p>
          <div className="tablewrap tall">
            <table>
              <thead><tr><th>Row</th><th>Status</th><th>Name</th><th>IP</th><th>Manufacturer</th><th>SNMP</th><th>Messages</th></tr></thead>
              <tbody>{preview.rows.map((r) => (
                <tr key={r.rowNumber} className={`row-${r.status.toLowerCase()}`}>
                  <td>{r.rowNumber}</td><td>{r.status}</td><td>{r.name}</td><td>{r.managementIp}</td><td>{r.manufacturerName}</td>
                  <td>{r.credentialProfile ? `profile: ${r.credentialProfile}` : r.snmpVersion}</td><td>{r.messages.join(' ')}</td>
                </tr>))}</tbody>
            </table>
          </div>
          <div className="buttons">
            <button className="btn primary" disabled={ok === 0 || commit.isPending} onClick={() => commit.mutate()}>{commit.isPending ? 'Committing…' : `Commit ${ok} switch(es)`}</button>
            <button className="btn" onClick={() => setPreview(null)}>Cancel</button>
          </div>
        </>
      )}
      {done && (
        <div className="alert success">
          ✓ Import complete: {done.created} created, {done.updated} updated, {done.skipped} skipped, {done.errors} errors.{' '}
          <Link to="/switches">View switches</Link>. <span className="muted">(“Scan all imported switches” becomes available with discovery in Phase 2.)</span>
        </div>
      )}
    </div>
  );
}

function ProfilesTab() {
  const qc = useQueryClient();
  const { data, error } = useQuery({ queryKey: ['profiles'], queryFn: api.listProfiles });
  const [editing, setEditing] = useState<ProfileDto | 'new' | null>(null);
  const del = useMutation({ mutationFn: api.deleteProfile, onSuccess: () => qc.invalidateQueries({ queryKey: ['profiles'] }) });
  return (
    <div>
      <p className="muted">Named SNMP credential sets that import rows and switches can reference. Secrets are encrypted and never shown again.</p>
      <div className="toolbar"><div className="spacer" /><button className="btn primary" onClick={() => setEditing('new')}>+ New profile</button></div>
      <ErrorBox error={error ?? del.error} />
      <div className="tablewrap">
        <table>
          <thead><tr><th>Name</th><th>SNMP</th><th>Description</th><th>Switches</th><th /></tr></thead>
          <tbody>
            {data?.map((p) => (
              <tr key={p.id}>
                <td>{p.name}</td>
                <td>{p.credential.version}{p.credential.version === 'V3' ? ` · ${p.credential.authProtocol}/${p.credential.privProtocol}` : ''}</td>
                <td>{p.description ?? ''}</td><td>{p.switchCount}</td>
                <td className="actions">
                  <button className="link" onClick={() => setEditing(p)}>Edit</button>
                  <button className="link danger" onClick={() => confirm(`Delete profile ${p.name}?`) && del.mutate(p.id)}>Delete</button>
                </td>
              </tr>))}
            {data?.length === 0 && <tr><td colSpan={5} className="muted">No profiles yet.</td></tr>}
          </tbody>
        </table>
      </div>
      {editing && <ProfileForm profile={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
    </div>
  );
}

function ProfileForm({ profile, onClose }: { profile: ProfileDto | null; onClose: () => void }) {
  const qc = useQueryClient();
  const [name, setName] = useState(profile?.name ?? '');
  const [description, setDescription] = useState(profile?.description ?? '');
  const [cred, setCred] = useState<CredentialInput>(profile ? { ...emptyCredential(), version: profile.credential.version, authProtocol: profile.credential.authProtocol, privProtocol: profile.credential.privProtocol } : emptyCredential());
  const save = useMutation({
    mutationFn: () => (profile ? api.updateProfile(profile.id, { name, description, credential: cred }) : api.createProfile({ name, description, credential: cred })),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ['profiles'] }); onClose(); },
  });
  return (
    <form className="form modal" onSubmit={(e) => { e.preventDefault(); save.mutate(); }}>
      <h3>{profile ? 'Edit profile' : 'New profile'}</h3>
      <Field label="Name"><input required value={name} onChange={(e) => setName(e.target.value)} placeholder="Core-v3" /></Field>
      <Field label="Description"><input value={description} onChange={(e) => setDescription(e.target.value)} /></Field>
      <CredentialFields value={cred} onChange={setCred} existing={profile?.credential} />
      <ErrorBox error={save.error} />
      <div className="buttons"><button className="btn primary" disabled={save.isPending}>Save</button><button type="button" className="btn" onClick={onClose}>Cancel</button></div>
    </form>
  );
}

const FIELDS: { key: keyof RuntimeSettings; label: string; hint: string; step?: number }[] = [
  { key: 'snmpTimeoutMs', label: 'SNMP timeout (ms)', hint: 'Per request. 500–60000.' },
  { key: 'snmpRetries', label: 'SNMP retries', hint: '0–5. Retries happen only after a timeout.' },
  { key: 'snmpMaxRepetitions', label: 'GETBULK max-repetitions', hint: '1–50. Lower it if a switch struggles with large tables.' },
  { key: 'maxParallelScans', label: 'Parallel switch scans', hint: '1–50. Limits load on the network and the database.' },
  { key: 'perVlanTimeoutMs', label: 'Per-VLAN timeout (ms)', hint: 'Cisco per-VLAN MAC queries use short timeouts so scans stay fast.' },
  { key: 'perVlanParallelism', label: 'Per-VLAN parallelism', hint: '1–16 simultaneous VLAN queries per switch.' },
  { key: 'offlineAfterMinutes', label: 'Location window (minutes)', hint: 'When a device was seen on several ports, only sightings this close to the newest one are compared. Does not affect Online/Offline.' },
  { key: 'staleAfterDays', label: 'Device stale after (days)', hint: 'Not found by the scans of its switch for longer than this → Stale.' },
  { key: 'metricsRetentionDays', label: 'Keep health and traffic history (days)', hint: '7 to 1095. Older data is deleted automatically.' },
  { key: 'dnsTimeoutMs', label: 'Reverse-DNS timeout (ms)', hint: 'A slow DNS never fails a scan; the name stays “Unknown Device”.' },
  { key: 'hostnameRefreshHours', label: 'Retry unresolved hostnames after (hours)', hint: '1–720.' },
  { key: 'uplinkHeuristicMinMacs', label: 'Uplink heuristic: minimum MACs', hint: 'Used only when LLDP/CDP are unavailable. Higher = more cautious.' },
  { key: 'uplinkHeuristicRatio', label: 'Uplink heuristic: overlap ratio', hint: '0.3–1.0. Fraction of a port’s MACs that must also be edge MACs elsewhere.', step: 0.05 },
];

const SCAN_CHOICES: [number, string][] = [[0, 'Off (scan only when I click Scan Now)'], [15, 'Every 15 minutes'], [30, 'Every 30 minutes'], [60, 'Every hour'], [120, 'Every 2 hours'], [360, 'Every 6 hours'], [720, 'Every 12 hours'], [1440, 'Once a day']];
const POLL_CHOICES: [number, string][] = [[0, 'Off (no CPU, memory, latency or traffic history; alerts cannot work)'], [5, 'Every 5 minutes (recommended)'], [15, 'Every 15 minutes'], [30, 'Every 30 minutes'], [60, 'Every hour']];

/** The two things that make the portal talk to the switches on its own. Both are choices the administrator makes here. */
function ScheduleCard({ values, onSaved }: { values: RuntimeSettings; onSaved: (r: RuntimeSettings) => void }) {
  const [scan, setScan] = useState(values.scanIntervalMinutes); const [poll, setPoll] = useState(values.healthPollMinutes); const [ok, setOk] = useState(false);
  useEffect(() => { setScan(values.scanIntervalMinutes); setPoll(values.healthPollMinutes); }, [values.scanIntervalMinutes, values.healthPollMinutes]);
  const save = useMutation({ mutationFn: () => api.saveSettings({ ...values, scanIntervalMinutes: scan, healthPollMinutes: poll }), onSuccess: (r) => { setOk(true); onSaved(r); } });
  const scanAll = useMutation({ mutationFn: api.scanAll });
  const opts = (list: [number, string][], cur: number) => (list.some(([v]) => v === cur) ? list : [...list, [cur, `Custom: every ${cur} minutes`] as [number, string]]);
  return (
    <div className="panelx" style={{ marginBottom: 20 }}>
      <h2>Scheduled scans</h2>
      <p className="hint">Nothing here runs unless you turn it on. Device status (Online / Offline) always reflects the <strong>most recent scan</strong> of the switch a device sits behind; it never changes just because time passes.</p>
      <form className="form" onSubmit={(e) => { e.preventDefault(); setOk(false); save.mutate(); }}>
        <div className="row">
          <Field label="Full discovery scan (devices, ports, VLANs, neighbors)" hint="Reads the MAC, ARP and interface tables of every switch. Off by default: no traffic until you click Scan Now.">
            <select value={scan} onChange={(e) => { setScan(Number(e.target.value)); setOk(false); }}>{opts(SCAN_CHOICES, scan).map(([v, l]) => <option key={v} value={v}>{l}</option>)}</select>
          </Field>
          <Field label="Health poll (uptime, CPU, memory, latency, port counters)" hint="A small read-only SNMP poll. Needed for the trend reports, the utilization gauges and e-mail alerts; it does not update device status.">
            <select value={poll} onChange={(e) => { setPoll(Number(e.target.value)); setOk(false); }}>{opts(POLL_CHOICES, poll).map(([v, l]) => <option key={v} value={v}>{l}</option>)}</select>
          </Field>
        </div>
        <ErrorBox error={save.error ?? scanAll.error} />
        {ok && <div className="alert success">✓ Schedule saved and applied.</div>}
        {scanAll.isSuccess && <div className="alert success">✓ A scan of every switch was queued.</div>}
        <div className="buttons"><button className="btn primary" disabled={save.isPending || (scan === values.scanIntervalMinutes && poll === values.healthPollMinutes)}>Save schedule</button>
          <button type="button" className="btn" disabled={scanAll.isPending} onClick={() => scanAll.mutate()}>Scan all switches now</button></div>
      </form>
    </div>
  );
}

function GeneralTab() {
  const qc = useQueryClient();
  const { data, error } = useQuery({ queryKey: ['settings'], queryFn: api.settings });
  const [form, setForm] = useState<RuntimeSettings | null>(null);
  const [saved, setSaved] = useState(false);
  const values = form ?? data;
  const save = useMutation({ mutationFn: () => api.saveSettings(values!), onSuccess: (s) => { setForm(s); setSaved(true); qc.setQueryData(['settings'], s); } });
  const fieldErrors = save.error instanceof Error && 'fieldErrors' in save.error ? (save.error as { fieldErrors: Record<string, string[]> }).fieldErrors : {};
  return (
    <div>
      {values && <ScheduleCard values={values} onSaved={(r) => { setForm(r); qc.setQueryData(['settings'], r); }} />}
      <h3>Discovery settings</h3>
      <p className="muted">Applied immediately and kept in the database; no restart needed. Connection string, ports, TLS and logging stay in <code>appsettings.json</code> (see CONFIGURATION.md).</p>
      <ErrorBox error={error ?? (Object.keys(fieldErrors).length ? null : save.error)} />
      {values && (
        <form className="form" onSubmit={(e) => { e.preventDefault(); setSaved(false); save.mutate(); }}>
          <div className="settings-grid">
            {FIELDS.map((f) => (
              <Field key={f.key} label={f.label} hint={fieldErrors[f.key]?.join(' ') ?? f.hint}>
                <input type="number" step={f.step ?? 1} value={values[f.key]} className={fieldErrors[f.key] ? 'invalid' : ''}
                  onChange={(e) => { setSaved(false); setForm({ ...values, [f.key]: Number(e.target.value) }); }} />
              </Field>
            ))}
          </div>
          {saved && <div className="alert success">✓ Settings saved and applied.</div>}
          <div className="buttons"><button className="btn primary" disabled={save.isPending}>{save.isPending ? 'Saving…' : 'Save settings'}</button></div>
        </form>
      )}
      <h3>Export</h3>
      <p>All devices: <a href="/api/export/devices?format=csv">CSV</a> · <a href="/api/export/devices?format=xlsx">Excel</a> &nbsp;(the Devices page exports the current filters). Switches without credentials: <a href="/api/admin/export/switches?format=csv">CSV</a> · <a href="/api/admin/export/switches?format=xlsx">Excel</a>.</p>
    </div>
  );
}
