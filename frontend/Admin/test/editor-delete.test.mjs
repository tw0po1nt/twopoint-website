import assert from 'node:assert/strict';
import { test } from 'node:test';

import { answer, apiAnswer, load, settle } from './load.mjs';

const noDetails = { author: null, category: null, tags: [], series: null, image: null, showOnHomepage: true };

const live = {
  slug: 'my-post',
  state: 'published',
  pendingEdit: false,
  publishDate: '2026-02-04T13:00:00Z',
  eTag: null,
  title: 'My post',
  excerpt: 'About it.',
  body: 'Body.\n',
  details: noDetails,
  text: '',
  published: { title: 'My post', excerpt: 'About it.', body: 'Body.\n', details: noDetails },
};

const ok = (data) => answer(200, { success: true, message: null, data });

const deletes = [];
let refuse = true;

const respond = (url, init = {}) => {
  if (url.endsWith('/api/internal/posts/my-post/deploy')) {
    return ok({ deploy: 'deployed' });
  }

  if (url.endsWith('/api/internal/posts/my-post') && init.method === 'DELETE') {
    const headers = new Headers(init.headers);
    deletes.push({ body: JSON.parse(init.body), ifMatch: headers.get('If-Match'), ifNoneMatch: headers.get('If-None-Match') });

    if (refuse) {
      refuse = false;
      return answer(422, { success: false, message: 'If you delete the post, a link in another post breaks. The admin does not delete the post.', data: null });
    }

    return ok({ productionRun: 7, warnings: ['comments'] });
  }

  if (url.endsWith('/api/internal/posts/my-post')) {
    return ok(live);
  }

  return apiAnswer(url);
};

await load('http://localhost:4322/posts/my-post', { respond });

const menuItem = (label) => [...document.querySelectorAll('#editor-menu [role="menuitem"]')].find((item) => item.textContent.trim().startsWith(label));
const dialog = () => document.querySelector('#delete-dialog');
const button = (label) => [...dialog().querySelectorAll('button')].find((item) => item.textContent.trim() === label);

const type = async (text) => {
  const input = document.querySelector('#delete-slug');
  input.value = text;
  input.dispatchEvent(new Event('input', { bubbles: true }));
  await settle();
};

test('Delete stays disabled until the typed text equals the slug', async () => {
  menuItem('Delete').click();
  await settle();
  assert.ok(dialog().open, 'The dialog is open');
  assert.equal(button('Delete').disabled, true);

  await type('my-pos');
  assert.equal(button('Delete').disabled, true);

  await type('my-post');
  assert.equal(button('Delete').disabled, false);
});

test('a refusal shows its reason in the dialog, and the next try deletes the post and leaves for the post list', async () => {
  button('Delete').click();
  await settle();

  assert.deepEqual(deletes, [{ body: { confirmSlug: 'my-post' }, ifMatch: null, ifNoneMatch: '*' }]);
  assert.ok(dialog().open, 'The dialog stays open');
  assert.match(dialog().textContent, /a link in another post breaks/);

  button('Delete').click();
  await settle();

  assert.equal(deletes.length, 2);
  assert.equal(window.location.pathname, '/posts');
  assert.match(document.body.textContent, /The comments of the post are still in the comment table/);
});
