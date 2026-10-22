import { GlobalRegistrator } from '@happy-dom/global-registrator';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

// Loads the built admin at a URL, with a stub `fetch` that records each call.
export const load = async (url) => {
  const manifest = JSON.parse(await readFile('build/manifest.json', 'utf8'));
  const config = JSON.parse(await readFile('config.json', 'utf8'))[manifest.mode];

  GlobalRegistrator.register({ url });
  document.body.innerHTML = '<div id="admin"><noscript>The admin needs JavaScript.</noscript></div>';

  const calls = [];
  globalThis.fetch = async (input) => {
    calls.push(String(input));
    throw new TypeError('The test has no network.');
  };

  await import(pathToFileURL(path.resolve('dist', manifest.script.slice(1))).href);
  return { calls, config };
};

export const heading = () => document.querySelector('main h1').textContent.trim();

export const links = () =>
  [...document.querySelectorAll('nav.navigation a')].map((link) => ({
    text: link.textContent.trim(),
    href: link.getAttribute('href'),
    current: link.getAttribute('aria-current'),
  }));

export const follow = (text) => {
  const link = [...document.querySelectorAll('a')].find((item) => item.textContent.trim() === text);
  link.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, button: 0 }));
};
