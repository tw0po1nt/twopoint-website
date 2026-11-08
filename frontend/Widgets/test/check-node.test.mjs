import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { check, problems } from '../../check-node.mjs';

const frontend = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');

test('the pinned Node and a newer Node pass', () => {
  assert.deepEqual(problems({ pin: '26', engines: '>=26', version: 'v26.0.0' }), []);
  assert.deepEqual(problems({ pin: '26', engines: '>=26', version: 'v27.1.2' }), []);
});

test('an older Node fails with the pin, the actual version, and nvm use', () => {
  const found = problems({ pin: '26', engines: '>=26', version: 'v24.22.1' });
  assert.equal(found.length, 1);
  assert.match(found[0], /Node 26 or newer/);
  assert.match(found[0], /v24\.22\.1/);
  assert.match(found[0], /nvm use/);
});

test('an engines range that disagrees with the pin fails', () => {
  const found = problems({ pin: '28', engines: '>=26', version: 'v28.0.0' });
  assert.equal(found.length, 1);
  assert.match(found[0], /engines\.node .* ">=26".* pins Node 28/);
});

test('a missing engines range fails', () => {
  const found = problems({ pin: '26', engines: undefined, version: 'v26.0.0' });
  assert.equal(found.length, 1);
  assert.match(found[0], /Set engines\.node to ">=26"/);
});

test('a pin that is not one major version fails', () => {
  const found = problems({ pin: 'lts/*', engines: '>=26', version: 'v26.0.0' });
  assert.equal(found.length, 1);
  assert.match(found[0], /one Node major version/);
});

test('the check reads the pin and the engines range of a package', () => {
  const dir = mkdtempSync(path.join(tmpdir(), 'check-node-'));

  try {
    writeFileSync(path.join(dir, '.node-version'), '26\n');
    writeFileSync(path.join(dir, 'package.json'), JSON.stringify({ engines: { node: '>=26' } }));
    assert.deepEqual(check(dir, 'v26.1.0'), []);
    assert.equal(check(dir, 'v24.22.1').length, 1);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

for (const pkg of ['Widgets', 'Admin']) {
  test(`${pkg} has a pin that agrees with its engines range, and engine-strict`, () => {
    const dir = path.join(frontend, pkg);
    assert.deepEqual(check(dir, 'v999.0.0'), []);
    assert.match(readFileSync(path.join(dir, '.npmrc'), 'utf8'), /^engine-strict=true$/m);
  });
}
