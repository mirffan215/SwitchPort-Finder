import type { ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api, type Branding } from '../api';

export function useBranding() { return useQuery({ queryKey: ['branding'], queryFn: api.branding, staleTime: 60_000, retry: false }); }
export const logoSrc = (b?: Branding) => (b?.hasLogo ? `/api/branding/logo?v=${b.version}` : null);

/** The company logo and/or name (set in Settings → Company). Links to the company address in a new tab when one is set. */
export function CompanyMark({ height = 36 }: { height?: number }) {
  const { data: b } = useBranding();
  const src = logoSrc(b);
  if (!src && !b?.companyName) return null;
  const inner: ReactNode = <>{src && <img src={src} alt={b?.companyName ? `${b.companyName} logo` : 'Company logo'} style={{ maxHeight: height, maxWidth: 160, objectFit: 'contain' }} />}{b?.companyName && <strong style={{ marginLeft: src ? 8 : 0 }}>{b.companyName}</strong>}</>;
  return b?.linkUrl
    ? <a href={b.linkUrl} target="_blank" rel="noopener noreferrer" className="companymark" title={`Open ${b.companyName ?? 'the company website'}`}>{inner}</a>
    : <span className="companymark">{inner}</span>;
}
