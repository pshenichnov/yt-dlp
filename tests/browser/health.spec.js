const { test, expect } = require('@playwright/test');
async function mode(request,value) { await request.get(`/__mode?value=${value}`); }
test('navigates home to status and back with keyboard', async ({page}) => { await page.goto('/'); await page.getByRole('link',{name:'Application health'}).focus(); await page.keyboard.press('Enter'); await expect(page).toHaveURL(/\/status$/); await expect(page.getByRole('heading',{name:'Application health'})).toBeVisible(); await page.getByRole('link',{name:'Video library'}).focus(); await page.keyboard.press('Enter'); await expect(page).toHaveURL(/\/$/); });
test('renders healthy, disabled, unhealthy and defensive states', async ({page,request}) => { await mode(request,'healthy'); await page.goto('/status'); await expect(page.getByText('Healthy',{exact:true}).first()).toBeVisible(); await expect(page.getByText('disabled',{exact:true})).toBeVisible(); await mode(request,'unhealthy'); await page.getByRole('button',{name:'Refresh now'}).click(); await expect(page.getByText('Unhealthy',{exact:true}).first()).toBeVisible(); await expect(page.getByText('extract: unhealthy · 0 consumers')).toBeVisible(); await mode(request,'malformed'); await page.getByRole('button',{name:'Refresh now'}).click(); await expect(page.getByText('No component detail reported.').first()).toBeVisible(); await expect(page.getByText('unknown',{exact:true}).first()).toBeVisible(); });
test('retains a stale report after failure and clears warning on recovery', async ({page,request}) => { await mode(request,'healthy'); await page.goto('/status'); await expect(page.getByText('Healthy',{exact:true}).first()).toBeVisible(); await mode(request,'unavailable'); await page.getByRole('button',{name:'Refresh now'}).click(); await expect(page.getByRole('status')).toContainText('may be stale'); await expect(page.getByText('Healthy',{exact:true}).first()).toBeVisible(); await mode(request,'healthy'); await page.getByRole('button',{name:'Refresh now'}).click(); await expect(page.getByRole('status')).toBeHidden(); });
test('remains usable at a narrow viewport', async ({page,request}) => { await mode(request,'healthy'); await page.setViewportSize({width:360,height:740}); await page.goto('/status'); await expect(page.getByRole('button',{name:'Refresh now'})).toBeInViewport(); await expect(page.getByRole('heading',{name:'Analyzer'})).toBeVisible(); });

function report() {
  return {
    status: 'healthy',
    checkedAt: '2026-11-24T12:00:00Z',
    services: { api: { status: 'healthy', durationMilliseconds: 12, description: 'API available' } }
  };
}

test('shows unavailable for invalid JSON and recovers with timestamp and durations', async ({ page }) => {
  let invalid = true;
  await page.route('**/health', route => route.fulfill({
    contentType: 'application/json', body: invalid ? '{invalid' : JSON.stringify(report())
  }));
  await page.goto('/status');
  await expect(page.locator('#overall-status')).toHaveText('Unavailable');
  await expect(page.getByRole('status')).not.toContainText('stale');
  invalid = false;
  await page.getByRole('button', { name: 'Refresh now' }).click();
  await expect(page.locator('#overall-status')).toHaveText('Healthy');
  await expect(page.locator('#checked-at')).toContainText('Last checked');
  await expect(page.getByText('Probe duration: 12 ms', { exact: true })).toBeVisible();
  await expect(page.getByRole('status')).toBeHidden();
});

test('renders untrusted descriptions and queue names as text', async ({ page }) => {
  const payload = report();
  const hostile = '<img src=x onerror="window.injected=true">';
  payload.services.api.description = hostile;
  payload.services.api.queues = { [hostile]: { status: 'healthy', consumers: 2 } };
  await page.route('**/health', route => route.fulfill({ json: payload }));
  await page.goto('/health.html');
  await expect(page.getByText(hostile, { exact: true })).toBeVisible();
  await expect(page.getByText(`${hostile}: healthy · 2 consumers`, { exact: true })).toBeVisible();
  await expect(page.locator('#components img')).toHaveCount(0);
  expect(await page.evaluate(() => window.injected)).toBeUndefined();
});

test('bounds requests and prevents overlapping refreshes', async ({ page }) => {
  await page.clock.install();
  let requests = 0;
  let releaseRequest;
  const pendingRequest = new Promise(resolve => { releaseRequest = resolve; });
  await page.route('**/health', async route => {
    requests++;
    await pendingRequest;
    await route.abort();
  });
  await page.goto('/status');
  await expect.poll(() => requests).toBe(1);
  await expect(page.getByRole('button', { name: 'Refreshing…' })).toBeDisabled();
  await page.evaluate(() => document.querySelector('#refresh').dispatchEvent(new Event('click')));
  expect(requests).toBe(1);
  await page.clock.fastForward(10001);
  await expect(page.getByRole('status')).toContainText('timed out');
  await expect(page.getByRole('button', { name: 'Refresh now' })).toBeEnabled();
  releaseRequest();
});

test('refreshes every 30 seconds only when enabled and visible', async ({ page }) => {
  await page.clock.install();
  let requests = 0;
  await page.route('**/health', route => { requests++; return route.fulfill({ json: report() }); });
  await page.goto('/status');
  await expect(page.locator('#overall-status')).toHaveText('Healthy');
  await page.clock.fastForward(30000);
  await expect.poll(() => requests).toBe(2);
  await expect(page.getByRole('button', { name: 'Refresh now' })).toBeEnabled();
  await page.getByLabel('Refresh every 30 seconds').uncheck();
  await page.clock.fastForward(30000);
  expect(requests).toBe(2);
  await page.getByLabel('Refresh every 30 seconds').check();
  await page.evaluate(() => Object.defineProperty(document, 'hidden', { configurable: true, value: true }));
  await page.clock.fastForward(30000);
  expect(requests).toBe(2);
  await page.evaluate(() => {
    Object.defineProperty(document, 'hidden', { configurable: true, value: false });
    document.dispatchEvent(new Event('visibilitychange'));
  });
  await expect.poll(() => requests).toBe(3);
});
