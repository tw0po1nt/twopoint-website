import assert from 'node:assert/strict';
import { test } from 'node:test';

import { answer, apiAnswer, deferred, load, settle } from './load.mjs';

const post = {
  slug: 'my-post',
  state: 'draft',
  pendingEdit: false,
  publishDate: null,
  eTag: '"v1"',
  title: 'My post',
  excerpt: 'About it.',
  body: 'Some text.\n',
  details: { author: null, category: null, tags: [], series: null, image: null, showOnHomepage: true },
  text: '',
  published: null,
};

const base = 'https://twopointwebsite.blob.core.windows.net/assets';
const uploads = [];
let pending = [];

const respond = (url, init = {}) => {
  if (url.endsWith('/api/internal/assets')) {
    const waiting = deferred();
    uploads.push({ method: init.method, body: init.body });
    pending.push(waiting);
    return waiting.promise;
  }

  if (url.endsWith('/api/internal/posts/my-post')) {
    return answer(200, { success: true, message: null, data: init.method === 'PUT' ? { eTag: '"v2"', check: { kind: 'found', problems: [] } } : post });
  }

  return apiAnswer(url);
};

await load('http://localhost:4322/posts/my-post', { respond });

const view = () => document.querySelector('.cm-content').cmTile.view;
const doc = () => view().state.doc.toString();
const placeholders = () => [...document.querySelectorAll('.cm-upload')].map((item) => item.textContent);
const notices = () => [...document.querySelectorAll('.editor-notices .alert')].map((item) => item.textContent.replace(/\s+/g, ' ').trim());

const reply = async (status, body) => {
  pending.shift().resolve(answer(status, body));
  await settle();
};

const paste = async (file) => {
  const event = new Event('paste', { bubbles: true, cancelable: true });
  Object.defineProperty(event, 'clipboardData', { value: { files: [file], getData: () => '' } });
  document.querySelector('.cm-content').dispatchEvent(event);
  await settle();
  return event;
};

const drop = async (file) => {
  const event = new Event('drop', { bubbles: true, cancelable: true });
  Object.defineProperty(event, 'dataTransfer', { value: { files: [file] } });
  document.querySelector('.cm-content').dispatchEvent(event);
  await settle();
  return event;
};

const press = (text) => [...document.querySelectorAll('.editor-notices button')].find((item) => item.textContent.trim() === text).click();

test('a pasted image shows a placeholder outside the text, and the URL replaces it with the cursor in the alt text', async () => {
  view().dispatch({ selection: { anchor: 5 } });
  const event = await paste(new File(['png'], 'cat.png', { type: 'image/png' }));

  assert.ok(event.defaultPrevented, 'The paste is the upload');
  assert.equal(uploads.length, 1);
  assert.equal(uploads[0].method, 'POST');
  assert.equal(uploads[0].body.name, 'cat.png');
  assert.deepEqual(placeholders(), ['Uploading cat.png…']);
  assert.equal(doc(), 'Some text.\n', 'The placeholder is not in the Markdown');

  await reply(200, { success: true, message: null, data: { name: '0123456789abcdef.png', url: `${base}/0123456789abcdef.png` } });

  assert.equal(doc(), `Some ![](${base}/0123456789abcdef.png)text.\n`);
  assert.deepEqual(placeholders(), []);
  assert.equal(view().state.selection.main.head, 5 + 2, 'The cursor is between the brackets');
  assert.deepEqual(notices(), []);
});

test('a paste of text is not an upload', async () => {
  const event = new Event('paste', { bubbles: true, cancelable: true });
  Object.defineProperty(event, 'clipboardData', { value: { files: [new File(['x'], 'a.png', { type: 'image/png' })], getData: () => 'cells' } });
  document.querySelector('.cm-content').dispatchEvent(event);
  await settle();
  assert.equal(uploads.length, 1, 'No second upload');
});

test('a dropped PDF becomes a ::pdf line between blank lines', async () => {
  view().dispatch({ selection: { anchor: view().state.doc.length } });
  await drop(new File(['%PDF-'], 'slides.pdf', { type: 'application/pdf' }));
  assert.equal(uploads.length, 2);

  await reply(200, { success: true, message: null, data: { name: 'aaaaaaaaaaaaaaaa.pdf', url: `${base}/aaaaaaaaaaaaaaaa.pdf` } });

  assert.ok(doc().endsWith(`\n\n::pdf{file="${base}/aaaaaaaaaaaaaaaa.pdf"}`), doc());
});

test('a failed upload removes the placeholder and shows a retry that sends the same file', async () => {
  await paste(new File(['png'], 'dog.png', { type: 'image/png' }));
  assert.deepEqual(placeholders(), ['Uploading dog.png…']);
  const before = doc();

  await reply(415, { success: false, message: 'Upload a JPEG, PNG, WebP, GIF, or PDF file', data: null });

  assert.deepEqual(placeholders(), [], 'The placeholder is gone');
  assert.equal(doc(), before, 'The text did not change');
  assert.equal(notices().length, 1);
  assert.match(notices()[0], /dog\.png did not upload\./);
  assert.match(notices()[0], /Upload a JPEG, PNG, WebP, GIF, or PDF file/);

  const file = uploads.at(-1).body;
  press('Retry');
  await settle();
  assert.equal(uploads.length, 4, 'A second send');
  assert.equal(uploads.at(-1).body, file, 'The same file');
  assert.deepEqual(placeholders(), ['Uploading dog.png…']);
  assert.deepEqual(notices(), []);

  await reply(200, { success: true, message: null, data: { name: 'bbbbbbbbbbbbbbbb.png', url: `${base}/bbbbbbbbbbbbbbbb.png` } });
  assert.ok(doc().includes(`![](${base}/bbbbbbbbbbbbbbbb.png)`));
  assert.deepEqual(placeholders(), []);
});

test('an SVG is refused with no upload, and the author can dismiss the notice', async () => {
  const sent = uploads.length;
  await drop(new File(['<svg/>'], 'logo.svg', { type: 'image/svg+xml' }));

  assert.equal(uploads.length, sent, 'No upload');
  assert.match(notices()[0], /logo\.svg is not a JPEG, PNG, WebP, GIF, or PDF file\./);
  assert.equal([...document.querySelectorAll('.editor-notices button')].some((item) => item.textContent.trim() === 'Retry'), false);

  press('Dismiss');
  await settle();
  assert.deepEqual(notices(), []);
});

test('the actions menu has Add image, and it opens the file picker for images and PDFs', async () => {
  const addImage = () => [...document.querySelectorAll('#editor-menu [role="menuitem"]')].find((item) => item.textContent.trim() === 'Add image');
  assert.ok(addImage());
  assert.equal(document.querySelector('button[aria-label="More actions"]').getAttribute('popovertarget'), 'editor-menu');

  const clicked = [];
  const click = HTMLInputElement.prototype.click;
  HTMLInputElement.prototype.click = function () {
    clicked.push(this);
  };

  addImage().click();
  HTMLInputElement.prototype.click = click;

  assert.equal(clicked.length, 1);
  assert.equal(clicked[0].type, 'file');
  assert.match(clicked[0].accept, /image\/jpeg.*application\/pdf/);
  assert.equal(clicked[0].multiple, true);

  const sent = uploads.length;
  Object.defineProperty(clicked[0], 'files', { value: [new File(['gif'], 'wave.gif', { type: 'image/gif' })] });
  clicked[0].dispatchEvent(new Event('change'));
  await settle();
  assert.equal(uploads.length, sent + 1, 'The picked file goes out');
  assert.deepEqual(placeholders(), ['Uploading wave.gif…']);
  await reply(200, { success: true, message: null, data: { name: 'dddddddddddddddd.gif', url: `${base}/dddddddddddddddd.gif` } });
});

test('a retry puts the image where the placeholder moved to, not where the paste happened', async () => {
  view().dispatch({ selection: { anchor: 0 } });
  await paste(new File(['png'], 'bird.png', { type: 'image/png' }));
  view().dispatch({ changes: { from: 0, insert: 'Intro. ' } });
  assert.deepEqual(placeholders(), ['Uploading bird.png…']);

  await reply(500, { success: false, message: 'The storage is busy', data: null });
  assert.deepEqual(placeholders(), []);

  press('Retry');
  await settle();
  assert.deepEqual(placeholders(), ['Uploading bird.png…']);

  await reply(200, { success: true, message: null, data: { name: 'cccccccccccccccc.png', url: `${base}/cccccccccccccccc.png` } });
  assert.ok(doc().startsWith(`Intro. ![](${base}/cccccccccccccccc.png)`), doc().slice(0, 90));
});
