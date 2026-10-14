import http from 'node:http';
import https from 'node:https';
import { readFile } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';
import { pathToFileURL } from 'node:url';

export function targets(config) {
  return config.buckets.flatMap(({ name, canary }) => {
    const endpoint = `${config.location}.your-objectstorage.com`;
    return [
      { style: 'virtual-host', base: `https://${name}.${endpoint}` },
      { style: 'path', base: `https://${endpoint}/${name}` },
    ].flatMap(({ style, base }) => [
      { bucket: name, style, kind: 'list', url: `${base}/?list-type=2` },
      { bucket: name, style, kind: 'canary', url: `${base}/${canary}` },
    ]);
  });
}

export async function runProbe(checks, log = console.log) {
  let failures = 0;
  for (const check of checks) {
    try {
      const status = await new Promise((resolve, reject) => {
        const url = new URL(check.url);
        const transport = url.protocol === 'https:' ? https : http;
        const request = transport.get(url, { agent: false }, response => {
          resolve(response.statusCode);
          response.destroy();
        });
        const deadline = setTimeout(() => request.destroy(new Error('request timed out')), 15_000);
        request.once('close', () => clearTimeout(deadline));
        request.once('error', reject);
      });
      const passed = status === 403;
      if (!passed) failures++;
      log(`${passed ? 'PASS' : 'FAIL'} ${check.bucket} ${check.style} ${check.kind}: HTTP ${status} (expected 403)`);
    } catch (error) {
      failures++;
      log(`FAIL ${check.bucket} ${check.style} ${check.kind}: ${error.message}`);
    }
  }
  log(`public exposure probe: ${checks.length - failures}/${checks.length} checks passed`);
  return failures === 0 ? 0 : 1;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  if (process.argv.includes('--self-test')) {
    const result = spawnSync(process.execPath, ['--test', new URL('./check-public-exposure.test.mjs', import.meta.url).pathname], { stdio: 'inherit' });
    process.exitCode = result.status ?? 1;
  } else {
    const config = JSON.parse(await readFile(new URL('./public-exposure.json', import.meta.url), 'utf8'));
    process.exitCode = await runProbe(targets(config));
  }
}
