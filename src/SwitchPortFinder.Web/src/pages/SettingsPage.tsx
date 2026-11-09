import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { api, type CredentialInput, type ImportCommit, type ImportPreview, type ProfileDto } from '../api';
import { ErrorBox, Field } from '../components/Bits';
import CredentialFields, { emptyCredential } from '../components/CredentialFields';

type Tab = 'profiles' | 'import' | 'general';

export default function SettingsPage() {
  const [tab, setTab] = useState<Tab>('import');
  return (
    <section>
      <h1>Settings</h1>
      <div className="tabs">
        <button className={tab === 'import' ? 'active' : ''} onClick={() => setTab('import')}>Admin · Import Switches</button>
        <button className={tab === 'profiles' ? 'active' : ''} onClick={() => setTab('profiles')}>Admin · Credential Profiles</button>
        <button className={tab === 'general' ? 'active' : ''} onClick={() => setTab('general')}>General</button>
      </div>
      {tab === 'import' && <ImportTab />}
      {tab === 'profiles' && <ProfilesTab />}
      {tab === 'general' && (
        <div>
          <h3>Export</h3>
          <p>All devices: <a href="/api/export/devices?format=csv">CSV</a> · <a href="/api/export/devices?format=xlsx">Excel</a> &nbsp;(the Devices page exports the current filters). Switches without credentials: <a href="/api/admin/export/switches?format=csv">CSV</a> · <a href="/api/admin/export/switches?format=xlsx">Excel</a>.</p>
          <h3>Configuration</h3>
          <p className="muted">Timeouts, retries, concurrency, offline/stale thresholds and the scan schedule are set in <code>appsettings.json</code> (see CONFIGURATION.md). A settings UI arrives in Phase 6.</p>
        </div>
      )}
    </section>
  );
}

function ImportTab() {
  const qc = useQueryClient();
  const [file, setFile] = useState<File | null>(null);
  const [mode, setMode] = useState<'SkipExisting' | 'UpdateExisting'>('SkipExisting');
  const [preview, setPreview] = useState<ImportPreview | null>(null);
  const [done, setDone] = useState<ImportCommit | null>(null);
  const prev = useMutation({ mutationFn: () => api.previewImport(file!, mode), onSuccess: (p) => { setPreview(p); setDone(null); } });
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
      <div className="row">
        <Field label="File (.csv or .xlsx)"><input type="file" accept=".csv,.xlsx" onChange={(e) => { setFile(e.target.files?.[0] ?? null); setPreview(null); setDone(null); }} /></Field>
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
