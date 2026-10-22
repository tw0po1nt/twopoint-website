import assert from 'node:assert/strict';
import { mkdtemp, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { after, before, test } from 'node:test';

import { serve } from '../server.mjs';

const config = JSON.parse(await readFile('staticwebapp.config.json', 'utf8'));
const index = await readFile('dist/index.html', 'utf8');
const bridge = await readFile('dist/redirect/index.html', 'utf8');
const manifest = JSON.parse(await readFile('build/manifest.json', 'utf8'));

let server;
let origin;

before(async () => {
  server = await serve({ root: 'dist', config, port: 0 });
  origin = `http://localhost:${server.address().port}`;
});

after(() => server.close());

const get = (pathname) => fetch(`${origin}${pathname}`, { redirect: 'manual' });

test('a deep path with no file gets index.html', async () => {
  for (const pathname of ['/', '/posts/x', '/comments', '/posts/x/']) {
    const response = await get(pathname);
    assert.equal(response.status, 200, pathname);
    assert.equal(await response.text(), index, pathname);
  }
});

test('each response has the X-Robots-Tag header', async () => {
  for (const pathname of ['/', '/posts/x', '/redirect/', manifest.script, '/bundle/nothing.js']) {
    const response = await get(pathname);
    assert.equal(response.headers.get('x-robots-tag'), 'noindex, nofollow', pathname);
  }
});

test('the bridge page has no cache and no COOP header', async () => {
  const response = await get('/redirect/');
  assert.equal(response.status, 200);
  assert.equal(await response.text(), bridge);
  assert.equal(response.headers.get('cache-control'), 'no-store');
  assert.equal(response.headers.get('cross-origin-opener-policy'), null);
});

test('the bridge path with no slash goes to the bridge page', async () => {
  const response = await get('/redirect');
  assert.equal(response.status, 301);
  assert.equal(response.headers.get('location'), '/redirect/');
});

test('a bundle file or a bridge path with no file gets a 404', async () => {
  for (const pathname of ['/bundle/nothing.js', '/redirect/nothing']) {
    const response = await get(pathname);
    assert.equal(response.status, 404, pathname);
  }
});

test('a bundle file gets its content type', async () => {
  const script = await get(manifest.script);
  assert.equal(script.status, 200);
  assert.match(script.headers.get('content-type'), /^text\/javascript/);

  const style = await get(manifest.style);
  assert.match(style.headers.get('content-type'), /^text\/css/);
});

test('a path before the first build gets a 503 and the server keeps running', async () => {
  const root = await mkdtemp(path.join(tmpdir(), 'admin-'));
  const empty = await serve({ root, config, port: 0 });

  try {
    for (const pathname of ['/', '/posts/x']) {
      const response = await fetch(`http://localhost:${empty.address().port}${pathname}`);
      assert.equal(response.status, 503, pathname);
    }
  } finally {
    empty.close();
    await rm(root, { recursive: true });
  }
});
