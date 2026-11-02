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
let checks = 0;
const plain = { outcome: 'plain', edits: [], problems: [] };
let checkAnswer = plain;

const respond = (url, init = {}) => {
  if (url.endsWith('/api/internal/posts/my-post/delete/check') && init.method === 'POST') {
    checks += 1;
    return ok(checkAnswer);
  }

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

const open = async () => {
  menuItem('Delete').click();
  await settle();
};

const keep = async () => {
  button('Keep').click();
  await settle();
};

const type = async (text) => {
  const input = document.querySelector('#delete-slug');
  input.value = text;
  input.dispatchEvent(new Event('input', { bubbles: true }));
  await settle();
};

test('the dialog checks the links one time when it opens, and Delete stays disabled until the typed text equals the slug', async () => {
  await open();
  assert.ok(dialog().open, 'The dialog is open');
  assert.equal(checks, 1, 'One check');
  assert.equal(button('Delete').disabled, true);

  await type('my-pos');
  assert.equal(button('Delete').disabled, true);

  await type('my-post');
  assert.equal(button('Delete').disabled, false);
  assert.equal(checks, 1, 'No check at each input');
  assert.doesNotMatch(dialog().textContent, /Other posts link to this post/);
  await keep();
});

test('a blocked check shows the problems and keeps the delete disabled', async () => {
  checkAnswer = {
    outcome: 'blocked',
    edits: [],
    problems: [{ severity: 'error', place: null, message: "The post 'c-post' links to the post and has no pending edit." }],
  };

  await open();
  await type('my-post');
  assert.match(dialog().textContent, /The admin cannot delete the post/);
  assert.match(dialog().textContent, /The post 'c-post' links to the post and has no pending edit/);
  assert.equal(button('Delete').disabled, true);
  await keep();
});

test('a publish and delete lists the posts and gives the number of edits on the button', async () => {
  checkAnswer = {
    outcome: 'publish-and-delete',
    edits: [
      { slug: 'a-post', title: 'A post', eTag: '"a1"' },
      { slug: 'c-post', title: 'C post', eTag: '"c1"' },
    ],
    problems: [],
  };

  await open();
  const titles = [...dialog().querySelectorAll('.delete-edits li')].map((item) => item.textContent.trim());
  assert.deepEqual(titles, ['A post', 'C post']);
  assert.equal(button('Publish 2 edits and delete').disabled, true);

  await type('my-post');
  assert.equal(button('Publish 2 edits and delete').disabled, false);
});

test('a refusal shows its reason and checks again, and the next try deletes the post and leaves for the post list', async () => {
  const checksBefore = checks;
  button('Publish 2 edits and delete').click();
  await settle();

  assert.deepEqual(deletes, [
    {
      body: {
        confirmSlug: 'my-post',
        edits: [
          { slug: 'a-post', ifMatch: '"a1"' },
          { slug: 'c-post', ifMatch: '"c1"' },
        ],
      },
      ifMatch: null,
      ifNoneMatch: '*',
    },
  ]);
  assert.ok(dialog().open, 'The dialog stays open');
  assert.match(dialog().textContent, /a link in another post breaks/);
  assert.equal(checks, checksBefore + 1, 'The check runs again');

  checkAnswer = plain;
  await keep();
  await open();
  await type('my-post');
  button('Delete').click();
  await settle();

  assert.equal(deletes.length, 2);
  assert.deepEqual(deletes[1].body, { confirmSlug: 'my-post', edits: [] });
  assert.equal(window.location.pathname, '/posts');
  assert.match(document.body.textContent, /The comments of the post are still in the comment table/);
});
