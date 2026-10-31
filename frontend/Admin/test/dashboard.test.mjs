import assert from 'node:assert/strict';
import { test } from 'node:test';

import { apiAnswer, heading, load, settle } from './load.mjs';

const reads = [];

const respond = (url, init) => {
  reads.push(new URL(url).pathname.replace(/^\/api\//, ''));
  return apiAnswer(url, init);
};

await load('http://localhost:4322/', { respond });

const cards = () =>
  [...document.querySelectorAll('main a.stat-link')].map((card) => ({
    title: card.querySelector('h3').textContent.trim(),
    count: card.querySelector('.stat').textContent.trim(),
    href: card.getAttribute('href'),
  }));

const click = (title) => {
  const card = [...document.querySelectorAll('main a.stat-link')].find(
    (item) => item.querySelector('h3').textContent.trim() === title,
  );
  card.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, button: 0 }));
};

const tab = (text) => [...document.querySelectorAll('main [role="tab"]')].find((item) => item.textContent.trim() === text);

test('the dashboard shows the to-do counts and the audience, and each count links to its page', () => {
  assert.equal(heading(), 'Dashboard');
  assert.deepEqual(
    [...document.querySelectorAll('main h2')].map((item) => item.textContent.trim()),
    ['To-do', 'Audience'],
  );

  assert.deepEqual(cards(), [
    { title: 'Moderation queue', count: '3', href: '/comments' },
    { title: 'Drafts', count: '1', href: '/posts' },
    { title: 'Pending edits', count: '2', href: '/posts' },
    { title: 'Deliveries to start', count: '0', href: '/posts' },
    { title: 'Active subscriptions', count: '12', href: '/subscriptions' },
  ]);

  assert.equal(
    document.querySelector('main a.stat-link .muted').textContent.trim(),
    '+3 new, −1 canceled in the last 30 days',
  );
});

test('a count opens its page with its filter', async () => {
  click('Pending edits');
  await settle();
  assert.equal(location.pathname, '/posts');
  assert.equal(tab('Pending edits').getAttribute('aria-selected'), 'true');
});

test('each visit to the dashboard reads the counts again', async () => {
  history.pushState(null, '', '/');
  window.dispatchEvent(new PopStateEvent('popstate'));
  await settle();
  assert.equal(reads.filter((path) => path === 'internal/dashboard').length, 2);
});

test('the active subscriptions open the subscriptions with the filter Active', async () => {
  click('Active subscriptions');
  await settle();
  assert.equal(location.pathname, '/subscriptions');
  assert.equal(tab('Active').getAttribute('aria-selected'), 'true');
});
