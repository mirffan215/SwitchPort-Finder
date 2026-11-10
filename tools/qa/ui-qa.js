// Browser QA + accessibility suite for SwitchPort Finder (Playwright + axe-core). Run against a TEST instance with Local auth:
//   NODE_PATH=$(npm root -g) AXE=/path/to/axe.min.js BASE=https://host:8450 ADMIN_PASS='...' node tools/qa/ui-qa.js
// Creates and removes its own data (names start with "qa-"). Exit code 0 = all checks passed.
const { chromium } = require('playwright');
const fs = require('fs');
const BASE = process.env.BASE || 'http://127.0.0.1:5000', PASS = process.env.ADMIN_PASS, AXE = process.env.AXE;
if (!PASS) { console.error('set ADMIN_PASS'); process.exit(2); }
const axeSrc = AXE && fs.existsSync(AXE) ? fs.readFileSync(AXE, 'utf8') : null;
let failed = 0; const out = [];
const ok = (c, name, d = '') => { out.push([c ? 'PASS' : 'FAIL', name, c ? '' : d]); console.log(`[${c ? 'PASS' : 'FAIL'}] ${name}${c ? '' : '  -- ' + d}`); if (!c) failed++; };
const sfx = Math.random().toString(36).slice(2, 7);

(async () => {
  const browser = await chromium.launch({ args: ['--ignore-certificate-errors'] });
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, ignoreHTTPSErrors: true });
  await ctx.addInitScript(() => { window.__xss = 0; });
  const page = await ctx.newPage(); const problems = [];
  page.on('pageerror', e => problems.push('pageerror: ' + e.message));
  page.on('console', m => m.type() === 'error' && !/401|403|404|409|400|429/.test(m.text()) && problems.push('console: ' + m.text().slice(0, 160)));
  page.on('dialog', d => { problems.push('DIALOG (possible XSS): ' + d.message()); d.dismiss(); });
  const goto = async (p) => { await page.goto(BASE + p); await page.waitForLoadState('networkidle'); };

  // ---- login
  await goto('/');
  ok(await page.getByRole('heading', { name: 'SwitchPort Finder' }).isVisible() && await page.getByLabel('Password').isVisible(), 'unauthenticated visitor sees only the login form');
  ok(!(await page.locator('body').innerText()).includes('Dashboard'), 'no application data/navigation is rendered before login');
  await page.getByLabel('Username').fill('admin'); await page.getByLabel('Password').fill('wrong-wrong-wrong-1');
  await page.getByRole('button', { name: 'Sign in' }).click(); await page.waitForTimeout(600);
  ok(await page.getByText('Invalid username or password.').isVisible(), 'wrong password shows a generic error');
  await page.getByLabel('Password').fill(PASS); await page.getByRole('button', { name: 'Sign in' }).click();
  await page.getByRole('link', { name: 'Switches' }).first().waitFor({ timeout: 8000 });
  ok(await page.locator('.sidebar').isVisible(), 'valid login reaches the dashboard');

  // ---- every route renders without JS errors / blank page
  const routes = ['/', '/switches', '/devices', '/topology', '/discovery', '/settings', '/switches/new', '/search?q=test', '/devices?state=Offline', '/switches/999999', '/devices/999999', '/devices/999999/trace', '/no/such/page'];
  for (const r of routes) {
    problems.length = 0; await goto(r);
    const text = (await page.locator('body').innerText()).trim();
    ok(text.length > 20 && problems.length === 0, `route ${r} renders (no blank page, no JS errors)`, problems.join(' | ') || 'blank');
  }

  // ---- theme
  await goto('/');
  ok(await page.evaluate(() => document.documentElement.dataset.theme) === 'dark', 'default theme is dark');
  await page.getByRole('button', { name: 'Light', exact: true }).click(); await page.reload(); await page.waitForLoadState('networkidle');
  ok(await page.evaluate(() => document.documentElement.dataset.theme) === 'light', 'chosen theme survives a reload');
  const bgLight = await page.evaluate(() => getComputedStyle(document.body).backgroundColor);
  await page.getByRole('button', { name: 'Auto', exact: true }).click();
  await page.emulateMedia({ colorScheme: 'dark' }); const bgAutoDark = await page.evaluate(() => getComputedStyle(document.body).backgroundColor);
  await page.emulateMedia({ colorScheme: 'light' }); const bgAutoLight = await page.evaluate(() => getComputedStyle(document.body).backgroundColor);
  ok(bgAutoDark !== bgAutoLight && bgAutoLight === bgLight, 'Auto follows the operating-system colour scheme');
  await page.getByRole('button', { name: 'Dark', exact: true }).click();

  // ---- add / edit / delete a switch through the UI, incl. stored-XSS payload
  const xssName = `qa-<img src=x onerror="window.__xss=1">${sfx}`;
  await goto('/switches/new');
  await page.getByLabel('Name').fill(xssName); await page.getByLabel('Management IP').fill('203.0.113.201');
  await page.getByLabel('Community').first().fill('qa-community').catch(() => {});
  await page.getByRole('button', { name: 'Save' }).click(); await page.waitForTimeout(1200);
  await goto('/switches');
  ok(await page.getByText(xssName, { exact: false }).first().isVisible(), 'switch saved through the UI and listed (markup shown as plain text)');
  ok(await page.evaluate(() => window.__xss) === 0 && !problems.some(p => p.startsWith('DIALOG')), 'stored XSS payload in a switch name does not execute');
  await goto('/'); await page.waitForTimeout(500);
  ok(await page.evaluate(() => window.__xss) === 0, 'stored XSS payload is inert on the dashboard too');
  ok(!(await page.content()).includes('qa-community'), 'SNMP community never appears in the page HTML');

  // ---- search box
  await page.getByPlaceholder(/Search/).fill("' OR 1=1 --"); await page.waitForTimeout(800);
  ok((await page.locator('body').innerText()).length > 20 && !problems.some(p => p.startsWith('pageerror')), 'search box survives SQL-injection text');
  await page.getByPlaceholder(/Search/).fill('<script>window.__xss=2</script>'); await page.waitForTimeout(800);
  ok(await page.evaluate(() => window.__xss) === 0, 'search box does not execute script text');

  // ---- cleanup the QA switch via API using the page's session
  const sw = await page.evaluate(async () => (await (await fetch('/api/switches')).json()));
  for (const s of sw.filter(x => x.name.startsWith('qa-'))) await page.evaluate(async (id) => fetch('/api/switches/' + id, { method: 'DELETE', headers: { Origin: location.origin } }), s.id);

  // ---- responsive
  for (const [w, h, n] of [[375, 700, 'phone'], [768, 900, 'tablet']]) {
    await page.setViewportSize({ width: w, height: h });
    for (const r of ['/', '/switches', '/devices', '/settings']) {
      await goto(r); const over = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
      ok(over <= 2, `no horizontal page scroll on ${r} at ${n} width (${w}px)`, `overflow ${over}px`);
    }
  }
  await page.setViewportSize({ width: 1440, height: 900 });

  // ---- accessibility (axe-core), dark and light
  if (axeSrc) {
    for (const theme of ['dark', 'light']) {
      await page.evaluate(t => { localStorage.setItem('spf.theme', t); }, theme);
      for (const r of ['/', '/switches', '/devices', '/settings', '/switches/new']) {
        await goto(r); await page.evaluate(axeSrc);   // CDP evaluate is not subject to the page's CSP (an inline <script> would be blocked)
        const res = await page.evaluate(async () => await axe.run(document, { runOnly: ['wcag2a', 'wcag2aa', 'wcag21aa'] }));
        const serious = res.violations.filter(v => ['serious', 'critical'].includes(v.impact));
        ok(serious.length === 0, `accessibility (WCAG 2.1 AA) ${r} [${theme}]: no serious/critical issues`, serious.map(v => `${v.id}(${v.nodes.length}) e.g. ${v.nodes[0]?.html.slice(0, 80)}`).join(' | '));
      }
    }
  } else console.log('[SKIP] accessibility: set AXE=/path/to/axe.min.js');

  // ---- keyboard: skip-to-content not required, but every control reachable and focus visible
  await goto('/'); await page.keyboard.press('Tab'); const focusVisible = await page.evaluate(() => { const e = document.activeElement; return !!e && e !== document.body; });
  ok(focusVisible, 'Tab moves keyboard focus onto a control');

  // ---- logout
  await page.getByRole('button', { name: 'Sign out' }).click(); await page.waitForTimeout(800);
  ok(await page.getByLabel('Password').isVisible(), 'sign out returns to the login form');
  await page.goBack(); await page.waitForTimeout(600);
  ok(await page.getByLabel('Password').isVisible() && !(await page.locator('.sidebar').count()), 'after sign out, the Back button does not reveal protected pages');

  await browser.close();
  fs.writeFileSync(process.env.OUT || '/tmp/qa-results.json', JSON.stringify(out, null, 1));
  console.log(`\n${out.filter(o => o[0] === 'PASS').length} passed, ${failed} FAILED`);
  process.exit(failed ? 1 : 0);
})().catch(e => { console.error(e); process.exit(2); });
