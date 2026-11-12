import { useEffect, useRef, useState, type FormEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api';
import { ErrorBox, Field } from './Bits';
import { logoSrc } from './Brand';

/** Admin · Company: name, website and logo shown in the sidebar and on the sign-in page. */
export default function CompanyTab() {
  const qc = useQueryClient();
  const { data } = useQuery({ queryKey: ['branding'], queryFn: api.branding });
  const [name, setName] = useState(''); const [url, setUrl] = useState(''); const [saved, setSaved] = useState(false);
  const file = useRef<HTMLInputElement>(null);
  useEffect(() => { if (data) { setName(data.companyName ?? ''); setUrl(data.linkUrl ?? ''); } }, [data]);
  const done = () => qc.invalidateQueries({ queryKey: ['branding'] });
  const save = useMutation({ mutationFn: () => api.saveBranding({ companyName: name.trim() || null, linkUrl: url.trim() || null }), onSuccess: () => { setSaved(true); done(); } });
  const up = useMutation({ mutationFn: (f: File) => api.uploadLogo(f), onSuccess: () => { if (file.current) file.current.value = ''; done(); } });
  const del = useMutation({ mutationFn: api.removeLogo, onSuccess: done });
  const src = logoSrc(data);
  return (
    <div className="form">
      <h3>Company</h3>
      <p className="hint">Shown at the top of the menu and on the sign-in page. The logo is stored inside the portal's own database (nothing is loaded from the internet).</p>
      <form onSubmit={(e: FormEvent) => { e.preventDefault(); save.mutate(); }}>
        <div className="row">
          <Field label="Company name"><input value={name} maxLength={100} onChange={(e) => { setName(e.target.value); setSaved(false); }} /></Field>
          <Field label="Company website (optional)" hint="Opens in a new tab when the logo or name is clicked. Must start with http:// or https://"><input value={url} maxLength={300} onChange={(e) => { setUrl(e.target.value); setSaved(false); }} inputMode="url" /></Field>
        </div>
        <ErrorBox error={save.error} />
        <div className="buttons"><button className="btn primary" disabled={save.isPending}>{save.isPending ? 'Saving…' : 'Save'}</button>{saved && <span className="ok">Saved.</span>}</div>
      </form>
      <h3 style={{ marginTop: 24 }}>Logo</h3>
      {src ? <p><img src={src} alt="Current logo" style={{ maxHeight: 64, maxWidth: 240, background: '#fff', padding: 6, borderRadius: 6, border: '1px solid var(--line)' }} /></p> : <p className="muted">No logo uploaded.</p>}
      <div className="buttons">
        <input ref={file} type="file" accept="image/png,image/jpeg,image/gif,image/webp" aria-label="Logo file" onChange={(e) => { const f = e.target.files?.[0]; if (f) up.mutate(f); }} />
        {data?.hasLogo && <button type="button" className="btn" onClick={() => del.mutate()} disabled={del.isPending}>Remove logo</button>}
      </div>
      <p className="hint">PNG, JPEG, GIF or WebP, up to 512 KB. A wide logo about 160 × 40 pixels looks best. SVG files are not accepted.</p>
      <ErrorBox error={up.error ?? del.error} />
    </div>
  );
}
