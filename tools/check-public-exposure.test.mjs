// Story: TASK-2.9
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import { runProbe, targets } from './check-public-exposure.mjs';

const config = JSON.parse(await readFile(new URL('./public-exposure.json', import.meta.url), 'utf8'));
const checks = targets(config);

test('both addressing styles check list and the private canary for every bucket', () => {
  assert.equal(checks.length, 12);
  for (const { name, canary } of config.buckets) {
    assert.deepEqual(checks.filter(check => check.bucket === name).map(check => check.url), [
      `https://${name}.nbg1.your-objectstorage.com/?list-type=2`,
      `https://${name}.nbg1.your-objectstorage.com/${canary}`,
      `https://nbg1.your-objectstorage.com/${name}/?list-type=2`,
      `https://nbg1.your-objectstorage.com/${name}/${canary}`,
    ]);
  }
});

async function withServer(statusAt, action) {
  const requests = [];
  const server = createServer((request, response) => {
    requests.push(request);
    response.writeHead(statusAt(requests.length - 1), { Location: '/redirect' });
    response.end('fake S3 response');
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  try {
    const local = checks.map((check, index) => ({ ...check, url: `http://127.0.0.1:${server.address().port}/${index}` }));
    await action(local, requests);
  } finally {
    await new Promise(resolve => server.close(resolve));
  }
}

test('403 on every request passes without authentication headers', async () => {
  await withServer(() => 403, async (local, requests) => {
    assert.equal(await runProbe(local, () => {}), 0);
    assert.equal(requests.length, 12);
    for (const request of requests) {
      assert.equal(request.method, 'GET');
      assert.equal(request.headers.authorization, undefined);
      assert.equal(request.headers.cookie, undefined);
      assert.equal(request.headers['x-amz-security-token'], undefined);
    }
  });
});

for (const status of [200, 404, 301, 500]) {
  for (const [index, check] of checks.entries()) {
    test(`${status} fails for ${check.bucket} ${check.style} ${check.kind}`, async () => {
      await withServer(position => position === index ? status : 403, async (local, requests) => {
        const messages = [];
        assert.equal(await runProbe(local, message => messages.push(message)), 1);
        assert.equal(requests.length, 12);
        assert.ok(messages.some(message => message.includes(`HTTP ${status}`)));
      });
    });
  }
}

test('connection failure fails closed', async () => {
  assert.equal(await runProbe([{ ...checks[0], url: 'http://127.0.0.1:1/' }], () => {}), 1);
});
