import { useState } from 'react';

export type Theme = 'dark' | 'light' | 'auto';
const OPTIONS: { v: Theme; label: string }[] = [{ v: 'dark', label: 'Dark' }, { v: 'light', label: 'Light' }, { v: 'auto', label: 'Auto' }];

function current(): Theme {
  const t = document.documentElement.getAttribute('data-theme');
  return t === 'light' || t === 'auto' ? t : 'dark';
}

/** Dark / Light / Auto (follows the operating system). The choice is remembered in this browser only. */
export default function ThemeSwitch() {
  const [theme, setTheme] = useState<Theme>(current);
  const pick = (t: Theme) => {
    setTheme(t);
    document.documentElement.setAttribute('data-theme', t);
    try { localStorage.setItem('spf.theme', t); } catch { /* storage unavailable: the choice just lasts for this page */ }
  };
  return (
    <div className="themesw" role="group" aria-label="Colour theme">
      {OPTIONS.map((o) => <button key={o.v} type="button" aria-pressed={theme === o.v} onClick={() => pick(o.v)}>{o.label}</button>)}
    </div>
  );
}
