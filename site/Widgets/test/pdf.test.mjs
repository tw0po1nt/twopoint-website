import { GlobalRegistrator } from '@happy-dom/global-registrator';
import { Path2D, createCanvas } from '@napi-rs/canvas';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { test } from 'node:test';
import { pathToFileURL } from 'node:url';

const manifest = JSON.parse(await readFile('dist/manifest.json', 'utf8'));

// happy-dom has no canvas drawing and no Path2D. This adapter gives each canvas a @napi-rs/canvas context of the
// canvas size.
const contexts = new WeakMap();
const canvasAdapter = {
  getContext: ({ canvas }, type) => {
    if (type !== '2d') {
      return null;
    }

    const known = contexts.get(canvas);

    if (known && known.canvas.width === canvas.width && known.canvas.height === canvas.height) {
      return known;
    }

    const context = createCanvas(canvas.width, canvas.height).getContext('2d');
    contexts.set(canvas, context);
    return context;
  },
  toDataURL: ({ canvas }) => contexts.get(canvas)?.canvas.toDataURL() ?? 'data:,',
  toBlob: (_, callback) => callback(null),
};

GlobalRegistrator.register({ url: 'http://localhost:4321/posts/a-talk/', settings: { canvasAdapter } });
globalThis.Path2D = Path2D;

const errors = [];
console.error = (...args) => errors.push(args.map(String).join(' '));

// A PDF of 200×100 pages. Each page is one color, so a pixel tells the page.
const stubPdf = (colors) => {
  const pages = colors.map((_, index) => 3 + index * 2);

  const objects = [
    '<< /Type /Catalog /Pages 2 0 R >>',
    `<< /Type /Pages /Kids [${pages.map((page) => `${page} 0 R`).join(' ')}] /Count ${colors.length} >>`,
    ...colors.flatMap((color, index) => {
      const content = `${color.join(' ')} rg 0 0 200 100 re f`;
      return [
        `<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 100] /Contents ${pages[index] + 1} 0 R >>`,
        `<< /Length ${content.length} >>\nstream\n${content}\nendstream`,
      ];
    }),
  ];

  let text = '%PDF-1.4\n';
  const offsets = objects.map((body, index) => {
    const offset = text.length;
    text += `${index + 1} 0 obj\n${body}\nendobj\n`;
    return offset;
  });

  const xref = text.length;
  text += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n`;
  text += offsets.map((offset) => `${String(offset).padStart(10, '0')} 00000 n \n`).join('');
  text += `trailer\n<< /Size ${objects.length + 1} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`;
  return new TextEncoder().encode(text);
};

const red = [1, 0, 0];
const blue = [0, 0, 1];
const file = 'https://files.test/slides.pdf';

const calls = [];
let answer;
globalThis.fetch = async (url, init) => {
  calls.push(String(url));
  return answer();
};

const pdfAnswer = (bytes) => () =>
  new Response(bytes, { status: 200, headers: { 'Content-Type': 'application/pdf', 'Content-Length': String(bytes.length) } });

const until = async (check, what) => {
  for (let tries = 0; tries < 200; tries += 1) {
    if (check()) {
      return;
    }

    await new Promise((resolve) => setTimeout(resolve, 10));
  }

  assert.fail(`The test waited for ${what}.`);
};

const entry = pathToFileURL(path.resolve('dist', manifest.entries.pdf.file)).href;
let loads = 0;

// The module loader runs a module only one time for each URL.
const open = async (mount) => {
  calls.length = 0;
  errors.length = 0;
  document.body.innerHTML = `<figure class="pdf">${mount}<a id="download" href="${file}" download>Download the PDF</a></figure>`;
  loads += 1;
  await import(`${entry}?load=${loads}`);
};

const mount = () => document.getElementById('mount');
const page = () => mount().querySelector('canvas.pdf-page');
const count = () => mount().querySelector('.pdf-count')?.textContent;
const status = () => mount().querySelector('.pdf-status');
const button = (text) => [...mount().querySelectorAll('.pdf-nav button')].find((item) => item.textContent === text);

const centerColor = () => {
  const canvas = page();
  const [r, g, b] = canvas.getContext('2d').getImageData(canvas.width / 2, canvas.height / 2, 1, 1).data;
  return [r, g, b];
};

const isColor = (color) => () => {
  const [r, g, b] = centerColor();
  return r === color[0] * 255 && g === color[1] * 255 && b === color[2] * 255;
};

test('the viewer draws the first page of the PDF, and the download link stays', async () => {
  answer = pdfAnswer(stubPdf([red, blue]));
  await open(`<div id="mount" data-widget="pdf" data-file="${file}"></div>`);
  await until(() => count() === 'page 1 of 2', 'the page count');
  await until(isColor(red), 'the first page');

  assert.deepEqual(calls, [file], 'The viewer gets the file of the mount');
  assert.equal(page().hidden, false, 'The page shows');
  assert.equal(page().width, 200, 'The page has the width of the PDF page');
  assert.equal(page().height, 100, 'The page has the height of the PDF page');
  assert.equal(button('[previous]').disabled, true, 'No page comes before the first page');
  assert.equal(button('[next]').disabled, false, 'A page comes after the first page');
  assert.equal(document.getElementById('download').getAttribute('href'), file, 'The download link stays');
  assert.deepEqual(errors, []);
});

test('the reader pages forward and back through the PDF', async () => {
  answer = pdfAnswer(stubPdf([red, blue]));
  await open(`<div id="mount" data-widget="pdf" data-file="${file}"></div>`);
  await until(isColor(red), 'the first page');

  button('[next]').click();
  await until(() => count() === 'page 2 of 2', 'the second page count');
  await until(isColor(blue), 'the second page');
  assert.equal(button('[next]').disabled, true, 'No page comes after the last page');
  assert.equal(button('[previous]').disabled, false, 'A page comes before the last page');

  button('[previous]').click();
  await until(() => count() === 'page 1 of 2', 'the first page count');
  await until(isColor(red), 'the first page again');
  assert.deepEqual(errors, []);
});

test('two fast clicks draw the last page that the reader asked for', async () => {
  answer = pdfAnswer(stubPdf([red, blue, red, blue]));
  await open(`<div id="mount" data-widget="pdf" data-file="${file}"></div>`);
  await until(isColor(red), 'the first page');

  button('[next]').click();
  button('[next]').click();
  button('[next]').click();
  await until(() => count() === 'page 4 of 4', 'the last page count');
  await until(isColor(blue), 'the last page');
  await new Promise((resolve) => setTimeout(resolve, 100));
  assert.equal(isColor(blue)(), true, 'An earlier page does not draw over the last page');
  assert.deepEqual(errors, []);
});

test('a PDF that does not load shows the error, and the download link stays', async () => {
  answer = () => new Response('Not found', { status: 404 });
  await open(`<div id="mount" data-widget="pdf" data-file="${file}"></div>`);
  await until(() => status()?.classList.contains('is-failed'), 'the error');

  assert.match(status().textContent, /did not load/);
  assert.equal(page().hidden, true, 'No page shows');
  assert.equal(mount().querySelector('.pdf-nav'), null, 'No page buttons');
  assert.equal(document.getElementById('download').getAttribute('href'), file, 'The download link stays');
  assert.equal(errors.length, 1, 'The viewer logs the error');
  assert.match(errors[0], /^pdf: the PDF did not load\./);
});

test('a bad mount keeps its static HTML, and the entry logs the widget and the reason', async () => {
  await open('<div id="mount" data-widget="pdf" data-file="http://files.test/slides.pdf"><p>static</p></div>');
  await new Promise((resolve) => setTimeout(resolve, 20));

  assert.equal(mount().innerHTML, '<p>static</p>');
  assert.deepEqual(calls, [], 'No request');
  assert.deepEqual(errors, ["pdf: The 'data-file' of the pdf mount is 'http://files.test/slides.pdf'. Give the https URL of a .pdf file."]);
});

test('no other entry loads pdf.js or the worker', async () => {
  const worker = (await readFile(new URL(entry), 'utf8')).match(/pdf\.worker-[0-9A-F]{8}\.js/)?.[0];
  assert.ok(worker, 'The Pdf entry names the hashed worker');
  await readFile(path.join('dist', worker));

  for (const [name, { file, imports }] of Object.entries(manifest.entries)) {
    if (name === 'pdf') {
      continue;
    }

    for (const item of [file, ...imports]) {
      const text = await readFile(path.join('dist', item), 'utf8');
      assert.equal(text.includes('pdf.worker-'), false, `${item} of ${name} names no worker`);
      assert.equal(text.includes('Setting up fake worker'), false, `${item} of ${name} has no pdf.js code`);
    }
  }
});
