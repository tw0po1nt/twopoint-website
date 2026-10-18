import { GlobalRegistrator } from '@happy-dom/global-registrator';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { test } from 'node:test';

const manifest = JSON.parse(await readFile('dist/manifest.json', 'utf8'));
const head = await readFile(path.join('dist', manifest.head), 'utf8');

GlobalRegistrator.register({ url: 'http://localhost:4321/' });

document.body.innerHTML = `
  <p id="past" data-hide-at="2000-01-01T00:00:00.000Z">past</p>
  <p id="future" data-hide-at="9999-12-31T23:59:59.999Z">future</p>
  <p id="bad" data-hide-at="2000-01-01T00:00:00Z">bad</p>`;

// The page runs the head script inline, before the parser gets to the body.
new Function(head)();

test('before DOMContentLoaded, the script hides no element', () => {
  assert.equal(document.getElementById('past').hidden, false);
});

test('after DOMContentLoaded, the script hides each element with a past date, and only those elements', () => {
  document.dispatchEvent(new Event('DOMContentLoaded'));
  assert.equal(document.getElementById('past').hidden, true, 'The past date hides its element');
  assert.equal(document.getElementById('future').hidden, false, 'The future date keeps its element');
  assert.equal(document.getElementById('bad').hidden, false, 'A date of another form keeps its element');
});
