import assert from 'node:assert/strict';
import { test } from 'node:test';

import { answer, apiAnswer, load } from './load.mjs';

const run = (repo, number) => ({
  number,
  state: 'failed',
  url: `https://github.com/tw0po1nt/${repo}/actions/runs/${number}`,
});

const respond = (url, init) =>
  url.endsWith('/api/internal/deploys') && init.method === 'GET'
    ? answer(200, {
        success: true,
        message: null,
        data: { deploy: 'failed', production: run('twopoint-website', 41), staging: run('twopoint-website-staging', 77) },
      })
    : apiAnswer(url);

await load('http://localhost:4322/', { respond });

const card = () => document.querySelector('main .card.rebuild');
const alerts = () =>
  [...card().querySelectorAll('.alert')].map((alert) => ({
    tone: [...alert.classList].filter((name) => name === 'bad' || name === 'warn'),
    title: alert.querySelector('.alert-title').textContent.trim(),
    link: alert.querySelector('a').getAttribute('href'),
  }));

test('the deploy state shows next to Rebuild site', () => {
  assert.equal(card().querySelector('h3 .badge').textContent.trim(), 'Deploy failed');
});

test('a failed production run shows with a link to the run, and a failed staging run shows as a warning', () => {
  assert.deepEqual(alerts(), [
    {
      tone: ['bad'],
      title: 'Production run #41 failed.',
      link: 'https://github.com/tw0po1nt/twopoint-website/actions/runs/41',
    },
    {
      tone: ['warn'],
      title: 'Staging run #77 failed.',
      link: 'https://github.com/tw0po1nt/twopoint-website-staging/actions/runs/77',
    },
  ]);
});
