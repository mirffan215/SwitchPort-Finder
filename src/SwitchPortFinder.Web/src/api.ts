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
export type NeighborConfidence = 'Unknown' | 'Endpoint' | 'LikelySwitch' | 'ConfirmedSwitch';
export interface Neighbor {
  neighborId: number; protocol: 'Lldp' | 'Cdp' | 'Inferred'; connectedSwitchId?: number | null; connectedSwitchName?: string | null; connectedSwitchIp?: string | null;
  remoteName?: string | null; remoteChassis?: string | null; remotePort?: string | null; remotePortIfIndex?: number | null; remoteIp?: string | null; platform?: string | null;
  confidence: NeighborConfidence; evidence?: string | null; capabilities: string; canAdd: boolean;
}
export interface PortRow { portId: number; ifIndex: number; name: string; alias?: string | null; status: 'UP' | 'DOWN'; type: PortType; speedMbps: number; vlan?: number | null; macCount: number; physAddress?: string | null; devices: PortDevice[]; neighbor?: Neighbor | null; supporting: string[]; traffic?: PortTraffic | null }
export interface PortTraffic { inBps?: number | null; outBps?: number | null; utilizationPct?: number | null; discardsPerSec: number; errorsPerSec: number; intervalSeconds: number; sampledAt: string; wide: boolean }
export interface TopPort { switchId: number; switchName: string; portId: number; ifIndex: number; portName: string; alias?: string | null; speedMbps: number; traffic: PortTraffic }
export interface SwitchUptime { switchId: number; name: string; managementIp: string; model?: string | null; isCore: boolean; status: string; uptimeSeconds?: number | null; sampledAt?: string | null }
export interface SwitchUptimeResponse { items: SwitchUptime[]; switchesTotal: number; withData: number }
export interface ManualDevice { mac?: string; name?: string; ip?: string; owner?: string; notes?: string; switchId?: number | null; portId?: number | null; vlan?: number | null }
export interface PortListItem { switchId: number; switchName: string; portId: number; ifIndex: number; name: string; alias?: string | null; status: 'UP' | 'DOWN'; type: PortType; speedMbps: number; vlan?: number | null; macCount: number; device?: string | null; traffic?: PortTraffic | null }
export interface ReportField { key: string; label: string; type: 'Text' | 'Number' | 'DateTime' | 'Bool'; unit?: string | null; measure: boolean }
export interface ReportDataset { key: string; label: string; description: string; history: boolean; fields: ReportField[] }
export interface ReportMeasure { field: string; agg: string }
export interface ReportFilter { field: string; op: string; value?: string | null }
export interface ReportQuery { dataset: string; columns?: string[]; groupBy?: string[]; measures?: ReportMeasure[]; filters?: ReportFilter[]; from?: string; to?: string; timeBucket?: string; sortBy?: string; desc?: boolean; limit?: number }
export interface ReportColumn { key: string; label: string; type: ReportField['type']; unit?: string | null }
export interface ReportResult { dataset: string; columns: ReportColumn[]; rows: (string | number | null)[][]; totalRows: number; truncated: boolean; generatedAt: string }
export interface SavedReport { id: number; name: string; json: string; createdBy?: string | null; updatedAt: string }
export type SmtpSecurity = 'None' | 'StartTls' | 'SslOnConnect';
export interface NotificationConfig { enabled: boolean; host: string; port: number; security: SmtpSecurity; username?: string | null; hasPassword: boolean; fromAddress: string; fromName: string; recipients: string[]; trustServerCertificate: boolean; publicBaseUrl?: string | null }
export interface NotificationConfigRequest { enabled: boolean; host: string; port: number; security: SmtpSecurity; username?: string | null; password?: string | null; fromAddress: string; fromName?: string; recipients: string[]; trustServerCertificate: boolean; publicBaseUrl?: string | null }
export type AlertRuleType = 'UplinkUtilization' | 'PortUtilization' | 'SwitchUnreachable' | 'HighCpu' | 'HighMemory' | 'HighLatency' | 'PacketDrops' | 'PortErrors' | 'SwitchRestarted';
export interface AlertRule { id: number; name: string; type: AlertRuleType; enabled: boolean; threshold: number; durationMinutes: number; switchId?: number | null; switchName?: string | null; cooldownMinutes: number; notifyOnResolve: boolean }
export type AlertRuleRequest = Omit<AlertRule, 'id' | 'switchName'>;
export interface AlertEvent { id: number; ruleId: number; ruleName: string; type: AlertRuleType; subject: string; title: string; detail: string; value?: number | null; state: 'Firing' | 'Resolved'; firstAt: string; lastAt: string; resolvedAt?: string | null; notifyCount: number }
export interface AlertsResponse { activeCount: number; active: AlertEvent[]; recent: AlertEvent[] }
export interface MailLogEntry { at: string; recipients: string; subject: string; success: boolean; error?: string | null }
export interface TopPortsResponse { metric: 'utilization' | 'drops'; items: TopPort[]; portsWithData: number; portsTotal: number }
export interface PortsResponse { summary: { total: number; connected: number; down: number; uplinks: number; lastScan?: string | null }; ports: PortRow[]; hasArpSource: boolean }
export interface DeviceRow { manual?: boolean; pinned?: boolean; owner?: string | null; notes?: string | null; vendor?: string | null; id: number; hostname: string; hostnameSource: string; ip?: string | null; mac: string; switchId?: number | null; switchName?: string | null; portId?: number | null; ifIndex?: number | null; portName?: string | null; vlan?: number | null; state: 'Active' | 'Offline' | 'Stale'; firstSeen: string; lastSeen: string; lowConfidence: boolean }
export interface Paged<T> { items: T[]; total: number; page: number; pageSize: number }

export interface SwitchHit { id: number; name: string; managementIp: string; manufacturerName: string; status: string; model?: string | null }
export interface PortHit { switchId: number; switchName: string; switchIp: string; portId: number; ifIndex: number; portName: string; alias?: string | null; status: string; type: PortType }
export interface SearchResponse { query: string; devices: DeviceRow[]; switches: SwitchHit[]; ports: PortHit[]; truncated: boolean }
export interface Sighting { switchId: number; switchName: string; portId: number; ifIndex: number; portName: string; isUplink: boolean; macsOnPort: number; vlan?: number | null; lastSeen: string }
export interface DeviceDetail { device: DeviceRow; ipSourceSwitch?: string | null; ipObservedAt?: string | null; locationNote: string; sightings: Sighting[] }
export interface TraceHop { switchId: number; switchName: string; switchIp: string; portId?: number | null; portName?: string | null; ifIndex?: number | null; linkConfidence?: NeighborConfidence | null; isFinal: boolean }
export interface TraceResult {
  hops: TraceHop[]; partial: boolean; message?: string | null;
  final?: { deviceId: number; hostname: string; ip?: string | null; mac: string; switchId: number; switchName: string; portName?: string | null; ifIndex?: number | null; vlan?: number | null; lowConfidence: boolean } | null;
}

export interface TopoNode { id: string; switchId?: number | null; name: string; ip?: string | null; vendor?: string | null; status?: string | null; external: boolean; isRoot: boolean; level: number; order: number; ports: number; uplinks: number; devices: number }
export interface TopoEdge { id: string; source: string; target: string; sourceSwitchId: number; sourcePortId: number; sourcePort: string; sourceIfIndex: number; targetSwitchId?: number | null; targetPort?: string | null; targetIfIndex?: number | null; confidence: NeighborConfidence; protocol: string }
export interface Topology { nodes: TopoNode[]; edges: TopoEdge[] }
export interface Dashboard { switches: number; devices: number; connectedPorts: number; switchUplinks: number; offlineDevices: number; lastDiscovery?: JobSummary | null; recentJobs: JobSummary[]; recentDevices: DeviceRow[]; totalPorts: number; downPorts: number }

export interface RuntimeSettings {
  snmpTimeoutMs: number; snmpRetries: number; snmpMaxRepetitions: number; maxParallelScans: number; perVlanTimeoutMs: number; perVlanParallelism: number;
  offlineAfterMinutes: number; staleAfterDays: number; scanIntervalMinutes: number; dnsTimeoutMs: number; hostnameRefreshHours: number; uplinkHeuristicMinMacs: number; uplinkHeuristicRatio: number; healthPollMinutes: number; metricsRetentionDays: number;
}

export type UserRole = 'Viewer' | 'Admin';
export interface Me { authRequired: boolean; authenticated: boolean; username?: string | null; role?: UserRole | null; mfaEnabled?: boolean; mfaSetupRequired?: boolean; mfaRequired?: boolean; mfaChallenge?: string | null }
export interface MfaSetup { secret: string; otpauthUri: string }
export interface UserDto { id: number; username: string; role: UserRole; disabled: boolean; locked: boolean; createdAt: string; lastLoginAt?: string | null; mfaEnabled?: boolean }

export class ApiError extends Error {
  constructor(public status: number, message: string, public fieldErrors: Record<string, string[]> = {}) { super(message); }
}

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const res = await fetch(url, init);
  if (res.status === 204) return undefined as T;
  const ct = res.headers.get('content-type') ?? '';
  const body = ct.includes('json') ? await res.json() : null;
  if (res.status === 401 && !url.startsWith('/api/auth/')) window.dispatchEvent(new Event('spf-unauthorized'));
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
  search: (q: string) => request<SearchResponse>(`/api/search?q=${encodeURIComponent(q)}`),
  addDevice: (r: ManualDevice) => request<DeviceDetail>('/api/devices', json('POST', r)),
  editDevice: (id: number, r: ManualDevice) => request<DeviceDetail>(`/api/devices/${id}/manual`, json('PUT', r)),
  deleteDevice: (id: number) => request<void>(`/api/devices/${id}`, { method: 'DELETE' }),
  device: (id: number) => request<DeviceDetail>(`/api/devices/${id}`),
  trace: (id: number) => request<TraceResult>(`/api/devices/${id}/trace`),
  me: () => request<Me>('/api/auth/me'),
  login: (username: string, password: string) => request<Me>('/api/auth/login', json('POST', { username, password })),
  logout: () => request<void>('/api/auth/logout', { method: 'POST' }),
  loginMfa: (challenge: string, code: string) => request<Me>('/api/auth/login/mfa', json('POST', { challenge, code })),
  mfaSetup: (password: string) => request<MfaSetup>('/api/auth/mfa/setup', json('POST', { password })),
  mfaEnable: (code: string) => request<{ recoveryCodes: string[] }>('/api/auth/mfa/enable', json('POST', { code })),
  mfaDisable: (password: string, code: string) => request<void>('/api/auth/mfa/disable', json('POST', { password, code })),
  mfaRecoveryCodes: (password: string, code: string) => request<{ recoveryCodes: string[] }>('/api/auth/mfa/recovery-codes', json('POST', { password, code })),
  changePassword: (currentPassword: string, newPassword: string) => request<void>('/api/auth/change-password', json('POST', { currentPassword, newPassword })),
  users: () => request<UserDto[]>('/api/users'),
  createUser: (u: { username: string; password: string; role: UserRole }) => request<UserDto>('/api/users', json('POST', u)),
  updateUser: (id: number, u: { role?: UserRole; disabled?: boolean; newPassword?: string; resetMfa?: boolean }) => request<UserDto>(`/api/users/${id}`, json('PUT', u)),
  deleteUser: (id: number) => request<void>(`/api/users/${id}`, { method: 'DELETE' }),
  settings: () => request<RuntimeSettings>('/api/settings'),
  saveSettings: (s: RuntimeSettings) => request<RuntimeSettings>('/api/settings', json('PUT', s)),
  topology: () => request<Topology>('/api/topology'),
  dashboard: () => request<Dashboard>('/api/dashboard'),
  allPorts: (p: { q?: string; status?: string; type?: string; switchId?: number; sort?: string; desc?: boolean; page?: number; pageSize?: number }) => {
    const u = new URLSearchParams(); Object.entries(p).forEach(([k, v]) => v !== undefined && v !== '' && u.set(k, String(v)));
    return request<Paged<PortListItem>>(`/api/ports?${u}`);
  },
  reportDatasets: () => request<ReportDataset[]>('/api/reports/datasets'),
  runReport: (q: ReportQuery) => request<ReportResult>('/api/reports/run', json('POST', q)),
  exportReport: async (q: ReportQuery, title: string, format: 'xlsx' | 'csv') => {
    const res = await fetch(`/api/reports/export?format=${format}`, { method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ query: q, title }) });
    if (!res.ok) throw new Error('Export failed.');
    return { blob: await res.blob(), name: /filename="?([^";]+)/.exec(res.headers.get('Content-Disposition') ?? '')?.[1] ?? `report.${format}` };
  },
  savedReports: () => request<SavedReport[]>('/api/reports'),
  saveReport: (name: string, jsonText: string) => request<SavedReport>('/api/reports', json('POST', { name, json: jsonText })),
  updateReport: (id: number, name: string, jsonText: string) => request<SavedReport>(`/api/reports/${id}`, json('PUT', { name, json: jsonText })),
  deleteReport: (id: number) => request<void>(`/api/reports/${id}`, { method: 'DELETE' }),
  notifConfig: () => request<NotificationConfig>('/api/notifications/config'),
  saveNotifConfig: (r: NotificationConfigRequest) => request<NotificationConfig>('/api/notifications/config', json('PUT', r)),
  testMail: (to?: string) => request<{ success: boolean; message: string }>('/api/notifications/test', json('POST', { to: to || null })),
  mailLog: () => request<MailLogEntry[]>('/api/notifications/log'),
  alertRules: () => request<AlertRule[]>('/api/notifications/rules'),
  createRule: (r: AlertRuleRequest) => request<AlertRule>('/api/notifications/rules', json('POST', r)),
  updateRule: (id: number, r: AlertRuleRequest) => request<AlertRule>(`/api/notifications/rules/${id}`, json('PUT', r)),
  deleteRule: (id: number) => request<void>(`/api/notifications/rules/${id}`, { method: 'DELETE' }),
  alerts: () => request<AlertsResponse>('/api/alerts'),
  switchUptime: () => request<SwitchUptimeResponse>('/api/metrics/switch-uptime?n=5'),
  topPorts: (metric: 'utilization' | 'drops', n = 5) => request<TopPortsResponse>(`/api/metrics/top-ports?metric=${metric}&n=${n}`),
  listProfiles: () => request<ProfileDto[]>('/api/credential-profiles'),
  createProfile: (r: { name: string; description?: string; credential: CredentialInput }) => request<ProfileDto>('/api/credential-profiles', json('POST', r)),
  updateProfile: (id: number, r: { name: string; description?: string; credential: CredentialInput }) => request<ProfileDto>(`/api/credential-profiles/${id}`, json('PUT', r)),
  deleteProfile: (id: number) => request<void>(`/api/credential-profiles/${id}`, { method: 'DELETE' }),
  /** Encrypted switch list WITH credentials. The file is generated on the server and is useless without the passphrase. */
  exportSecure: async (password: string, passphrase: string) => {
    const res = await fetch('/api/admin/export/switches/secure', { method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ password, passphrase }) });
    if (!res.ok) { let msg = 'Export failed.'; try { const j = await res.json(); msg = Object.values<string[]>(j.errors ?? {}).flat().join(' ') || j.title || msg; } catch { /* keep default */ } throw new Error(msg); }
    const name = /filename="?([^";]+)/.exec(res.headers.get('Content-Disposition') ?? '')?.[1] ?? 'switches-with-credentials.spfenc';
    return { blob: await res.blob(), name };
  },
  previewImport: (file: File, mode: string, passphrase?: string) => {
    const f = new FormData(); f.append('file', file); if (passphrase) f.append('passphrase', passphrase);
    return request<ImportPreview>(`/api/admin/import/switches/preview?mode=${mode}`, { method: 'POST', body: f });
  },
  commitImport: (previewId: string, mode: string) => request<ImportCommit>('/api/admin/import/switches/commit', json('POST', { previewId, mode })),
};
