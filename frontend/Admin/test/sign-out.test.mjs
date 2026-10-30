import assert from 'node:assert/strict';
import { test } from 'node:test';

import { load, press, pressEscape, settle, spyOnShowModal } from './load.mjs';

const { events } = await load('http://localhost:4322/');
const modals = spyOnShowModal();
const dialog = document.getElementById('sign-out');
const accountMenu = document.getElementById('account-menu');

test('Sign out in the account menu opens the dialog as a modal', async () => {
  press('Sign out', accountMenu);
  await settle();
  assert.deepEqual(modals, ['sign-out']);
  assert.equal(dialog.open, true);
  assert.equal(document.getElementById(dialog.getAttribute('aria-labelledby')).textContent.trim(), 'Sign out?');
});

test('Esc closes the dialog, and the author stays signed in', async () => {
  pressEscape(dialog);
  await settle();
  assert.equal(dialog.open, false);
  assert.deepEqual(events, ['start', 'token', 'token', 'token']);
});

test('after Esc, Sign out opens the dialog again', async () => {
  press('Sign out', accountMenu);
  await settle();
  assert.deepEqual(modals, ['sign-out', 'sign-out']);
  assert.equal(dialog.open, true);
});

test('Cancel closes the dialog', async () => {
  press('Cancel', dialog);
  await settle();
  assert.equal(dialog.open, false);
  assert.deepEqual(events, ['start', 'token', 'token', 'token']);
});

test('Sign out in the dialog closes the dialog and sends the page to the Entra sign-out', async () => {
  press('Sign out', accountMenu);
  await settle();
  press('Sign out', dialog);
  await settle();
  assert.equal(dialog.open, false);
  assert.deepEqual(events, ['start', 'token', 'token', 'token', 'signOut']);
});
