import { GlobalRegistrator } from '@happy-dom/global-registrator';
import { Path2D, createCanvas } from '@napi-rs/canvas';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { test } from 'node:test';
import { pathToFileURL } from 'node:url';

const manifest = JSON.parse(await readFile('dist/manifest.json', 'utf8'));

// happy-dom has no canvas drawing and no Path2D, so each canvas gets a @napi-rs/canvas context of its size.
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

// happy-dom has no layout and its ResizeObserver does nothing, so the test sets the size of an element and calls each observer of it.
const observers = [];
globalThis.ResizeObserver = class {
  constructor(callback) {
    this.callback = callback;
    this.targets = [];
    observers.push(this);
  }

  observe(target) {
    this.targets.push(target);
  }

  unobserve(target) {
    this.targets = this.targets.filter((item) => item !== target);
  }

  disconnect() {
    this.targets = [];
  }
};

const resize = (element, width, height) => {
  Object.defineProperty(element, 'clientWidth', { configurable: true, value: width });
  Object.defineProperty(element, 'clientHeight', { configurable: true, value: height });

  for (const observer of observers.filter((item) => item.targets.includes(element))) {
    observer.callback([{ target: element, contentRect: { width, height } }], observer);
  }
};

const setPixelRatio = (ratio) => Object.defineProperty(window, 'devicePixelRatio', { configurable: true, value: ratio });

const errors = [];
console.error = (...args) => errors.push(args.map(String).join(' '));

const pageObject = (index) => 3 + index * 2;

// One color per page, so a pixel identifies the page. A page can also have a text and link annotations.
const stubPdf = (colors, { texts = [], links = [] } = {}) => {
  const font = '/Resources << /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> >>';

  const objects = [
    '<< /Type /Catalog /Pages 2 0 R >>',
    `<< /Type /Pages /Kids [${colors.map((_, index) => `${pageObject(index)} 0 R`).join(' ')}] /Count ${colors.length} >>`,
    ...colors.flatMap((color, index) => {
      const text = texts[index] ? ` BT /F1 12 Tf 10 40 Td (${texts[index]}) Tj ET` : '';
      const content = `${color.join(' ')} rg 0 0 200 100 re f${text}`;
      const annots = links[index] ? ` /Annots [${links[index].join(' ')}]` : '';
      return [
        `<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 100] ${font}${annots} /Contents ${pageObject(index) + 1} 0 R >>`,
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

const uriLink = (url) => `<< /Type /Annot /Subtype /Link /Rect [0 0 100 50] /Border [0 0 0] /A << /S /URI /URI (${url}) >> >>`;
const pageLink = (index) => `<< /Type /Annot /Subtype /Link /Rect [100 50 200 100] /Border [0 0 0] /Dest [${pageObject(index)} 0 R /Fit] >>`;

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
const frame = () => mount().querySelector('.pdf-frame');
const page = () => mount().querySelector('canvas.pdf-page');
const textLayer = () => mount().querySelector('.textLayer');
const links = () => [...mount().querySelectorAll('.annotationLayer a')];
const scale = () => frame().style.getPropertyValue('--total-scale-factor');
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
  assert.equal(frame().hidden, false, 'The page shows');
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

test('a change of width draws the current page again at the new width', async (context) => {
  context.after(() => setPixelRatio(1));
  answer = pdfAnswer(stubPdf([red, blue]));
  await open(`<div id="mount" data-widget="pdf" data-file="${file}"></div>`);
  await until(isColor(red), 'the first page');
  button('[next]').click();
  await until(isColor(blue), 'the second page');

  setPixelRatio(2);
  resize(page(), 320.5, 160);
  await until(() => page().width === 641, 'the new width');

  assert.equal(page().height, 320, 'The page keeps the shape of the PDF page');
  await until(isColor(blue), 'the second page at the new width');
  assert.equal(count(), 'page 2 of 2', 'The page number does not change');
  assert.deepEqual(errors, []);
});

test('a change of height only does not draw the page again', async (context) => {
  context.after(() => setPixelRatio(1));
  answer = pdfAnswer(stubPdf([red, blue]));
  await open(`<div id="mount" data-widget="pdf" data-file="${file}"></div>`);
  await until(isColor(red), 'the first page');
  resize(page(), 200, 100);
  await new Promise((resolve) => setTimeout(resolve, 50));

  // A draw now gives a bitmap of twice the width, so a bitmap of the old width shows that no draw occurred.
  setPixelRatio(2);
  resize(page(), 200, 150);
  await new Promise((resolve) => setTimeout(resolve, 100));

  assert.equal(page().width, 200, 'The page does not draw again');
  assert.equal(isColor(red)(), true, 'The first page stays');
  assert.deepEqual(errors, []);
});

test('the text of the page is on the page for the reader to select, and changes with the page', async () => {
  answer = pdfAnswer(stubPdf([red, blue], { texts: ['First slide', 'Second slide'] }));
  await open(`<div id="mount" data-widget="pdf" data-file="${file}"></div>`);
  await until(() => textLayer().textContent === 'First slide', 'the text of the first page');

  assert.equal(textLayer().parentElement, frame(), 'The text is in the frame of the page');
  assert.equal(textLayer().querySelector('span').textContent, 'First slide', 'Each run of text is a span');

  button('[next]').click();
  await until(() => textLayer().textContent === 'Second slide', 'the text of the second page');
  assert.deepEqual(errors, []);
});

test('a link to a URL opens the URL, and a link to a page shows that page', async () => {
  const target = 'https://example.com/talk';
  answer = pdfAnswer(stubPdf([red, blue], { links: [[uriLink(target), pageLink(1)]] }));
  await open(`<div id="mount" data-widget="pdf" data-file="${file}"></div>`);
  await until(() => links().length === 2, 'the links of the first page');

  const external = links().find((link) => link.getAttribute('href') === target);
  assert.ok(external, 'The link has the URL');
  assert.match(external.rel, /noopener/, 'The target gets no access to the post');

  links().find((link) => link.getAttribute('href') === '#').click();
  await until(() => count() === 'page 2 of 2', 'the page of the link');
  await until(isColor(blue), 'the second page');
  await until(() => links().length === 0, 'no links on the second page');

  button('[previous]').click();
  await until(isColor(red), 'the first page again');
  await until(() => links().length === 2, 'the links of the first page again');
  await new Promise((resolve) => setTimeout(resolve, 50));
  assert.equal(links().length, 2, 'A link does not show two times');
  assert.deepEqual(errors, []);
});

test('a change of width scales the text and the links to the new width', async () => {
  answer = pdfAnswer(stubPdf([red], { texts: ['First slide'], links: [[uriLink('https://example.com/talk')]] }));
  await open(`<div id="mount" data-widget="pdf" data-file="${file}"></div>`);
  await until(() => links().length === 1 && textLayer().textContent === 'First slide', 'the text and the link');
  assert.equal(scale(), '1', 'The layers have the scale of the PDF page');

  resize(page(), 400, 200);
  await until(() => scale() === '2', 'the scale of the new width');
  await until(() => links().length === 1 && textLayer().textContent === 'First slide', 'the text and the link again');
  await new Promise((resolve) => setTimeout(resolve, 50));
  assert.equal(textLayer().textContent, 'First slide', 'The text does not show two times');
  assert.equal(links().length, 1, 'The link does not show two times');
  assert.deepEqual(errors, []);
});

test('a PDF that does not load shows the error, and the download link stays', async () => {
  answer = () => new Response('Not found', { status: 404 });
  await open(`<div id="mount" data-widget="pdf" data-file="${file}"></div>`);
  await until(() => status()?.classList.contains('is-failed'), 'the error');

  assert.match(status().textContent, /did not load/);
  assert.equal(frame().hidden, true, 'No page shows');
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
