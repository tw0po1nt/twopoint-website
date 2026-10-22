import { GlobalRegistrator } from '@happy-dom/global-registrator';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

export const posts = [
  { slug: 'first', title: 'First', createdDate: '2026-01-02T00:00:00Z', commentStats: { new: 2, approved: 5, rejected: 0 } },
  { slug: 'second', title: 'Second', createdDate: '2026-02-03T00:00:00Z', commentStats: { new: 1, approved: 0, rejected: 1 } },
];

const answer = (status, body) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

export const postsAnswer = () => answer(200, { success: true, message: null, data: posts });

// The stub sign-in has a session and gives a token at once. A test replaces a member to change that.
export const stubSignIn = (members = {}) => {
  const events = [];

  return {
    events,
    port: {
      start: async () => {
        events.push('start');
        return true;
      },
      signIn: async () => {
        events.push('signIn');
      },
      token: async () => {
        events.push('token');
        return 'token-1';
      },
      signInByPopup: async () => {
        events.push('signInByPopup');
      },
      ...members,
    },
  };
};

export const deferred = () => {
  let resolve;
  let reject;
  const promise = new Promise((yes, no) => {
    resolve = yes;
    reject = no;
  });
  return { promise, resolve, reject };
};

export const settle = async () => {
  for (let round = 0; round < 5; round += 1) {
    await new Promise((resolve) => setTimeout(resolve, 0));
  }
};

// The admin entry starts MSAL, so each test starts the app entry with a stub sign-in instead.
export const load = async (url, { signIn = stubSignIn(), respond = postsAnswer } = {}) => {
  const manifest = JSON.parse(await readFile('build/manifest.json', 'utf8'));
  const config = JSON.parse(await readFile('config.json', 'utf8'))[manifest.mode];

  GlobalRegistrator.register({ url });
  document.body.innerHTML = '<div id="admin"><noscript>The admin needs JavaScript.</noscript></div>';

  const calls = [];
  globalThis.fetch = async (input, init = {}) => {
    calls.push({ url: String(input), authorization: new Headers(init.headers).get('Authorization') });
    return respond(String(input));
  };

  const app = await import(pathToFileURL(path.resolve('dist', manifest.app.slice(1))).href);
  app.start(signIn.port);
  await settle();
  return { calls, config, events: signIn.events };
};

export const heading = () => document.querySelector('main h1').textContent.trim();

export const banner = () => document.querySelector('main .banner')?.textContent.trim() ?? null;

export const summary = () => [...document.querySelectorAll('.summary dd')].map((item) => item.textContent.trim());

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

export const press = (text) => {
  const button = [...document.querySelectorAll('button')].find((item) => item.textContent.trim() === text);
  button.click();
};
