import type { AuthProtocol, CredentialInput, CredentialSummary, PrivProtocol } from '../api';
import { Field } from './Bits';

/** Shows only the fields relevant to the selected SNMP version. Secret inputs are write-only: when a credential
 *  already exists they show an "unchanged" placeholder and are only sent when something is typed. */
export default function CredentialFields({ value, onChange, existing }: {
  value: CredentialInput; onChange: (v: CredentialInput) => void; existing?: CredentialSummary | null;
}) {
  const set = (p: Partial<CredentialInput>) => onChange({ ...value, ...p });
  const same = existing && existing.version === value.version;
  const ph = (has?: boolean) => (same && has ? '•••••• unchanged' : '');
  return (
    <>
      <Field label="SNMP version">
        <select value={value.version} onChange={(e) => set({ version: e.target.value as CredentialInput['version'] })}>
          <option value="V2c">SNMPv2c</option><option value="V3">SNMPv3</option>
        </select>
      </Field>
      {value.version === 'V2c' ? (
        <Field label="Community" hint="Use a read-only community.">
          <input type="password" autoComplete="new-password" value={value.community ?? ''} placeholder={ph(existing?.hasCommunity)} onChange={(e) => set({ community: e.target.value })} />
        </Field>
      ) : (
        <>
          <Field label="Username">
            <input value={value.username ?? ''} autoComplete="off" placeholder={ph(existing?.hasUsername)} onChange={(e) => set({ username: e.target.value })} />
          </Field>
          <div className="row">
            <Field label="Authentication protocol">
              <select value={value.authProtocol} onChange={(e) => set({ authProtocol: e.target.value as AuthProtocol })}>
                <option value="None">None</option><option value="Md5">MD5</option><option value="Sha1">SHA</option>
                <option value="Sha256">SHA-256</option><option value="Sha384">SHA-384</option><option value="Sha512">SHA-512</option>
              </select>
            </Field>
            {value.authProtocol !== 'None' && (
              <Field label="Authentication password" hint="Minimum 8 characters.">
                <input type="password" autoComplete="new-password" value={value.authPassword ?? ''} placeholder={ph(existing?.hasAuthPassword)} onChange={(e) => set({ authPassword: e.target.value })} />
              </Field>
            )}
          </div>
          {value.authProtocol !== 'None' && (
            <div className="row">
              <Field label="Privacy protocol">
                <select value={value.privProtocol} onChange={(e) => set({ privProtocol: e.target.value as PrivProtocol })}>
                  <option value="None">None</option><option value="Des">DES</option><option value="Aes128">AES-128</option>
                  <option value="Aes192">AES-192</option><option value="Aes256">AES-256</option>
                </select>
              </Field>
              {value.privProtocol !== 'None' && (
                <Field label="Privacy password" hint="Minimum 8 characters.">
                  <input type="password" autoComplete="new-password" value={value.privPassword ?? ''} placeholder={ph(existing?.hasPrivPassword)} onChange={(e) => set({ privPassword: e.target.value })} />
                </Field>
              )}
            </div>
          )}
          <Field label="Context name (optional)"><input value={value.contextName ?? ''} onChange={(e) => set({ contextName: e.target.value })} /></Field>
        </>
      )}
    </>
  );
}

export const emptyCredential = (): CredentialInput => ({ version: 'V2c', authProtocol: 'None', privProtocol: 'None' });
