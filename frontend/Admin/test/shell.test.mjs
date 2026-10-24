import assert from 'node:assert/strict';
import { test } from 'node:test';

import { count, follow, heading, links, load, press, settle, spyOnShowModal, summary, tabs } from './load.mjs';

const { calls, config } = await load('http://localhost:4322/');

test('the shell shows the side navigation, marks the dashboard, and shows the dashboard', () => {
  assert.equal(document.querySelector('#admin noscript'), null, 'The fallback text is gone');

  assert.deepEqual(links(), [
    { text: 'Dashboard', href: '/', current: 'page' },
    { text: 'Posts', href: '/posts', current: null },
    { text: 'Moderation queue', href: '/comments', current: null },
    { text: 'Commenters', href: '/commenters', current: null },
    { text: 'Series', href: '/series', current: null },
    { text: 'Subscriptions', href: '/subscriptions', current: null },
    { text: 'Settings', href: '/settings', current: null },
  ]);

  assert.equal(heading(), 'Dashboard');
});

test('the tab bar of a phone has Home, Posts, Queue, Readers, and More, and marks Home', () => {
  assert.deepEqual(tabs(), [
    { text: 'Home', href: '/', current: 'page' },
    { text: 'Posts', href: '/posts', current: null },
    { text: 'Queue', href: '/comments', current: null },
    { text: 'Readers', href: '/subscriptions', current: null },
    { text: 'More', href: null, current: null },
  ]);
});

test('the queue shows the count of new comments in the navigation and in the tab bar', () => {
  assert.equal(count('nav.side-nav a[href="/comments"]'), '3');
  assert.equal(count('nav.tab-bar a[href="/comments"]'), '3');
  assert.equal(count('nav.side-nav a[href="/posts"]'), null);
});

test('the account shows the name of the signed-in account', () => {
  assert.equal(document.querySelector('.side .account .strong').textContent.trim(), 'Ada Lovelace');
  assert.equal(document.querySelector('.side .account .avatar').textContent.trim(), 'AL');
});

test('the start gets the posts and the moderation queue from the API with the token, and shows the counts', () => {
  assert.deepEqual(calls, [
    { url: `${config.api}/api/internal/posts`, authorization: 'Bearer token-1' },
    { url: `${config.api}/api/internal/comments`, authorization: 'Bearer token-1' },
  ]);
  assert.deepEqual(summary(), ['2', '3'], 'Two posts and three new comments');
});

test('a link in the navigation shows its page and changes the URL with no page load', () => {
  follow('Moderation queue');
  assert.equal(location.pathname, '/comments');
  assert.equal(heading(), 'Moderation queue');
  assert.deepEqual(links().filter((link) => link.current === 'page').map((link) => link.text), ['Moderation queue']);
});

test('a link with a modifier key stays with the browser', () => {
  const link = document.querySelector('nav.side-nav a[href="/series"]');
  const event = new MouseEvent('click', { bubbles: true, cancelable: true, button: 0, metaKey: true });
  link.dispatchEvent(event);
  assert.equal(event.defaultPrevented, false, 'The browser opens the link');
  assert.equal(heading(), 'Moderation queue', 'The page does not change');
});

test('the back and forward buttons show the page of the URL', () => {
  history.pushState(null, '', '/subscriptions');
  window.dispatchEvent(new PopStateEvent('popstate'));
  assert.equal(heading(), 'Subscriptions');
});

test('Settings shows the API and the admin origins of the build mode', () => {
  follow('Settings');
  assert.equal(location.pathname, '/settings');
  const values = [...document.querySelectorAll('.settings dd')].map((item) => item.textContent.trim());
  assert.deepEqual(values, [config.api, config.origin]);
});

test('an unknown path shows that no page has the address, and links to the dashboard', () => {
  history.pushState(null, '', '/nothing/here');
  window.dispatchEvent(new PopStateEvent('popstate'));
  assert.equal(heading(), 'Page not found');
  follow('dashboard');
  assert.equal(location.pathname, '/');
  assert.equal(heading(), 'Dashboard');
});

test('More opens a sheet as a modal with the other pages, the site, and Sign out', async () => {
  const modals = spyOnShowModal();
  press('More', document.querySelector('nav.tab-bar'));
  await settle();
  const sheet = document.getElementById('more-sheet');
  assert.deepEqual(modals, ['more-sheet']);
  assert.equal(sheet.open, true);

  assert.deepEqual(
    [...sheet.querySelectorAll('.more-link')].map((item) => [item.textContent.trim(), item.getAttribute('href')]),
    [
      ['Commenters', '/commenters'],
      ['Series', '/series'],
      ['Settings', '/settings'],
      ['Open twopoint.dev', 'https://twopoint.dev'],
      ['Sign out', null],
    ],
  );
});

test('a link in the More sheet closes the sheet, shows its page, and marks More', async () => {
  const sheet = document.getElementById('more-sheet');
  follow('Series', sheet);
  await settle();
  assert.equal(sheet.open, false);
  assert.equal(location.pathname, '/series');
  assert.equal(heading(), 'Series');
  assert.deepEqual(tabs().filter((tab) => tab.current === 'page').map((tab) => tab.text), ['More']);
});

test('a change of page calls the API no more', () => {
  assert.equal(calls.length, 2);
});
