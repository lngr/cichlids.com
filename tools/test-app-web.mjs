#!/usr/bin/env node
// Exercise the staging browser build against the local workload images.
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';

const require = createRequire(new URL('../app/mobile/package.json', import.meta.url));
const { chromium } = require('playwright');
const browser = await chromium.launch({ args: ['--no-sandbox'] });
try {
  const page = await browser.newPage();
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.route('https://staging.cichlids.com/**', async route => {
    const url = new URL(route.request().url());
    const upstream = url.pathname.startsWith('/api/') ? process.env.WORKLOAD_API_URL : process.env.WORKLOAD_WEB_URL;
    const response = await route.fetch({ url: upstream + url.pathname + url.search });
    await route.fulfill({ response });
  });
  const pictures = page.waitForResponse(response => response.url().startsWith('https://staging.cichlids.com/api/pictures'));
  await page.goto('https://staging.cichlids.com/', { waitUntil: 'networkidle' });
  assert.equal((await pictures).status(), 200);
  await page.getByText('Newest', { exact: true }).waitFor();
  const session = page.waitForResponse(response => response.url() === 'https://staging.cichlids.com/api/auth/session');
  await page.goto('https://staging.cichlids.com/me', { waitUntil: 'networkidle' });
  assert.equal((await session).status(), 200);
  await page.getByText('Log in', { exact: true }).first().waitFor();
  assert.deepEqual(errors, []);
  console.log('browser: staging web loads; same-origin /api/pictures and /api/auth/session return 200; sign-in screen renders');
} finally {
  await browser.close();
}
