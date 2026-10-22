import assert from 'node:assert/strict';
import { test } from 'node:test';

import { follow, heading, links, load } from './load.mjs';

const { calls, config } = await load('http://localhost:4322/');

test('the admin replaces the mount with the navigation and the dashboard', () => {
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

test('a link in the navigation shows its page and changes the URL with no page load', () => {
  follow('Moderation queue');
  assert.equal(location.pathname, '/comments');
  assert.equal(heading(), 'Moderation queue');
  assert.deepEqual(links().filter((link) => link.current === 'page').map((link) => link.text), ['Moderation queue']);
});

test('a link with a modifier key stays with the browser', () => {
  const link = document.querySelector('nav.navigation a[href="/series"]');
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

test('the admin calls no API', () => {
  assert.deepEqual(calls, []);
});
