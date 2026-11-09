// Thin typed API client. Secrets are write-only: no response type contains a secret value.
export type Manufacturer =
  | 'AutoDetect' | 'Cisco' | 'HpeAruba' | 'Dell' | 'Juniper' | 'ExtremeNetworks' | 'Huawei' | 'MikroTik'
  | 'Ubiquiti' | 'Fortinet' | 'Netgear' | 'TpLink' | 'AlliedTelesis' | 'OtherGeneric';

export const MANUFACTURERS: { value: Manufacturer; label: string }[] = [
  { value: 'AutoDetect', label: 'Auto-detect' }, { value: 'Cisco', label: 'Cisco' }, { value: 'HpeAruba', label: 'HPE / Aruba' },
  { value: 'Dell', label: 'Dell' }, { value: 'Juniper', label: 'Juniper' }, { value: 'ExtremeNetworks', label: 'Extreme Networks' },
  { value: 'Huawei', label: 'Huawei' }, { value: 'MikroTik', label: 'MikroTik' }, { value: 'Ubiquiti', label: 'Ubiquiti' },
  { value: 'Fortinet', label: 'Fortinet (FortiSwitch)' }, { value: 'Netgear', label: 'Netgear' }, { value: 'TpLink', label: 'TP-Link' },
  { value: 'AlliedTelesis', label: 'Allied Telesis' }, { value: 'OtherGeneric', label: 'Other / Generic SNMP' },
];

export type SnmpVersion = 'V2c' | 'V3';
export type AuthProtocol = 'None' | 'Md5' | 'Sha1' | 'Sha256' | 'Sha384' | 'Sha512';
export type PrivProtocol = 'None' | 'Des' | 'Aes128' | 'Aes192' | 'Aes256';

export interface CredentialInput {
  version: SnmpVersion; community?: string; username?: string;
  authProtocol: AuthProtocol; authPassword?: string; privProtocol: PrivProtocol; privPassword?: string; contextName?: string;
}
export interface CredentialSummary {
  version: SnmpVersion; authProtocol: AuthProtocol; privProtocol: PrivProtocol; contextName?: string | null;
  hasCommunity: boolean; hasUsername: boolean; hasAuthPassword: boolean; hasPrivPassword: boolean;
}
export interface SwitchSummary {
  id: number; name: string; managementIp: string; manufacturer: Manufacturer; manufacturerName: string;
  detectedManufacturer?: Manufacturer | null; vendorMismatch: boolean; model?: string | null;
  status: 'Unknown' | 'Reachable' | 'Unreachable'; lastScanAt?: string | null; useAsArpSource: boolean;
  location?: string | null; credentialProfileId?: number | null; credentialProfileName?: string | null; portCount: number;
}
export interface SwitchDetail extends Omit<SwitchSummary, 'portCount'> {
  manufacturerOther?: string | null; notes?: string | null; sysName?: string | null; sysDescr?: string | null;
  sysObjectId?: string | null; credential?: CredentialSummary | null; createdAt: string; updatedAt: string;
}
export interface SwitchRequest {
  name: string; managementIp: string; manufacturer: Manufacturer; manufacturerOther?: string; model?: string;
  location?: string; notes?: string; useAsArpSource: boolean; credentialProfileId?: number | null; credential?: CredentialInput | null;
}
export interface TestResult {
  success: boolean; message: string; errorCode?: string | null; sysName?: string | null; sysDescr?: string | null;
  sysObjectId?: string | null; uptimeTicks?: number | null; detectedManufacturerName?: string | null; vendorMismatch: boolean; elapsedMs: number;
}
export interface ProfileDto { id: number; name: string; description?: string | null; credential: CredentialSummary; switchCount: number; updatedAt: string }
export interface ImportRow { rowNumber: number; name: string; managementIp: string; manufacturerName: string; snmpVersion: string; credentialProfile?: string | null; status: 'New' | 'Update' | 'Skipped' | 'Error'; messages: string[] }
export interface ImportPreview { previewId: string; fileName: string; mode: string; total: number; new: number; update: number; skipped: number; errors: number; rows: ImportRow[] }
export interface ImportCommit { batchId: number; total: number; created: number; updated: number; skipped: number; errors: number; switchIds: number[] }

export type PortType = 'Unknown' | 'Endpoint' | 'SwitchUplink' | 'Down';
export type JobStatus = 'Queued' | 'Running' | 'Completed' | 'CompletedWithWarnings' | 'Failed';
export interface StepResult { name: string; state: 'Pending' | 'Running' | 'Ok' | 'Warning' | 'Failed' | 'Skipped'; message?: string | null }
export interface JobSummary { id: number; switchId: number; switchName: string; status: JobStatus; queuedAt: string; startedAt?: string | null; completedAt?: string | null; durationSeconds?: number | null; portsFound: number; devicesFound: number; neighborsFound: number; errorCount: number; summary?: string | null }
export interface JobDetail { job: JobSummary; steps: StepResult[]; errors: { step: string; code: string; message: string; createdAt: string }[] }
export interface PortDevice { deviceId: number; hostname: string; ip?: string | null; mac: string; vlan?: number | null; lastSeen: string; state: 'Active' | 'Offline' | 'Stale'; lowConfidence: boolean }
export interface PortRow { portId: number; ifIndex: number; name: string; alias?: string | null; status: 'UP' | 'DOWN'; type: PortType; speedMbps: number; vlan?: number | null; macCount: number; physAddress?: string | null; devices: PortDevice[] }
export interface PortsResponse { summary: { total: number; connected: number; down: number; uplinks: number; lastScan?: string | null }; ports: PortRow[]; hasArpSource: boolean }
export interface DeviceRow { id: number; hostname: string; hostnameSource: string; ip?: string | null; mac: string; switchId?: number | null; switchName?: string | null; portId?: number | null; ifIndex?: number | null; portName?: string | null; vlan?: number | null; state: 'Active' | 'Offline' | 'Stale'; firstSeen: string; lastSeen: string; lowConfidence: boolean }
export interface Paged<T> { items: T[]; total: number; page: number; pageSize: number }

export class ApiError extends Error {
  constructor(public status: number, message: string, public fieldErrors: Record<string, string[]> = {}) { super(message); }
}

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const res = await fetch(url, init);
  if (res.status === 204) return undefined as T;
  const ct = res.headers.get('content-type') ?? '';
  const body = ct.includes('json') ? await res.json() : null;
  if (!res.ok) {
    const msg = body?.detail ?? body?.title ?? `Request failed (${res.status})`;
    throw new ApiError(res.status, msg, body?.errors ?? {});
  }
  return body as T;
}
const json = (method: string, data?: unknown): RequestInit => ({
  method, headers: { 'Content-Type': 'application/json' }, body: data === undefined ? undefined : JSON.stringify(data),
});

export const api = {
  listSwitches: () => request<SwitchSummary[]>('/api/switches'),
  getSwitch: (id: number) => request<SwitchDetail>(`/api/switches/${id}`),
  createSwitch: (r: SwitchRequest) => request<SwitchDetail>('/api/switches', json('POST', r)),
  updateSwitch: (id: number, r: SwitchRequest) => request<SwitchDetail>(`/api/switches/${id}`, json('PUT', r)),
  deleteSwitch: (id: number) => request<void>(`/api/switches/${id}`, { method: 'DELETE' }),
  testUnsaved: (r: { managementIp: string; manufacturer: Manufacturer; credentialProfileId?: number | null; credential?: CredentialInput | null; switchId?: number }) =>
    request<TestResult>('/api/switches/test', json('POST', r)),
  testSaved: (id: number) => request<TestResult>(`/api/switches/${id}/test`, { method: 'POST' }),
  scan: (id: number) => request<{ jobId: number }>(`/api/switches/${id}/scan`, { method: 'POST' }),
  scanAll: () => request<{ jobIds: number[] }>('/api/switches/scan-all', { method: 'POST' }),
  ports: (id: number) => request<PortsResponse>(`/api/switches/${id}/ports`),
  job: (id: number) => request<JobDetail>(`/api/discovery/jobs/${id}`),
  jobs: (switchId?: number) => request<JobSummary[]>(`/api/discovery/jobs?take=100${switchId ? `&switchId=${switchId}` : ''}`),
  devices: (p: Record<string, string | number | boolean | undefined>) =>
    request<Paged<DeviceRow>>('/api/devices?' + new URLSearchParams(Object.entries(p).filter(([, v]) => v !== undefined && v !== '').map(([k, v]) => [k, String(v)]))),
  listProfiles: () => request<ProfileDto[]>('/api/credential-profiles'),
  createProfile: (r: { name: string; description?: string; credential: CredentialInput }) => request<ProfileDto>('/api/credential-profiles', json('POST', r)),
  updateProfile: (id: number, r: { name: string; description?: string; credential: CredentialInput }) => request<ProfileDto>(`/api/credential-profiles/${id}`, json('PUT', r)),
  deleteProfile: (id: number) => request<void>(`/api/credential-profiles/${id}`, { method: 'DELETE' }),
  previewImport: (file: File, mode: string) => {
    const f = new FormData(); f.append('file', file);
    return request<ImportPreview>(`/api/admin/import/switches/preview?mode=${mode}`, { method: 'POST', body: f });
  },
  commitImport: (previewId: string, mode: string) => request<ImportCommit>('/api/admin/import/switches/commit', json('POST', { previewId, mode })),
};
