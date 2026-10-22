import assert from 'node:assert/strict';
import { access, readFile } from 'node:fs/promises';
import path from 'node:path';
import { test } from 'node:test';

const index = await readFile('dist/index.html', 'utf8');
const bridge = await readFile('dist/redirect/index.html', 'utf8');
const config = JSON.parse(await readFile('dist/staticwebapp.config.json', 'utf8'));

test('each page tells search engines not to index it', () => {
  for (const page of [index, bridge]) {
    assert.match(page, /<meta name="robots" content="noindex,nofollow">/);
  }
});

test('index.html loads each bundle file from an absolute path that exists', async () => {
  const sources = [...index.matchAll(/(?:src|href)="(\/bundle\/[^"]+)"/g)].map((match) => match[1]);
  assert.equal(sources.length, 2, 'A script and a stylesheet');

  for (const source of sources) {
    await access(path.join('dist', source));
  }
});

test('the host config sends noindex, keeps the bridge page out of the cache, and sends no COOP header', () => {
  assert.deepEqual(config.globalHeaders, { 'X-Robots-Tag': 'noindex, nofollow' });
  assert.deepEqual(config.routes, [{ route: '/redirect/*', headers: { 'Cache-Control': 'no-store' } }]);
  assert.equal(JSON.stringify(config).toLowerCase().includes('cross-origin-opener-policy'), false);
  assert.deepEqual(config.navigationFallback, { rewrite: '/index.html', exclude: ['/redirect/*', '/bundle/*'] });
});
