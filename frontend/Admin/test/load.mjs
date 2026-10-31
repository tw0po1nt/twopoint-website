import { GlobalRegistrator } from '@happy-dom/global-registrator';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

export const posts = [
  { slug: 'first', title: 'First', state: 'published', pendingEdit: false, publishDate: '2026-01-02T00:00:00Z', deploy: 'deployed', deliveryToStart: false, commentStats: { new: 2, approved: 5, rejected: 0 } },
  { slug: 'second', title: 'Second', state: 'published', pendingEdit: false, publishDate: '2026-02-03T00:00:00Z', deploy: 'deployed', deliveryToStart: false, commentStats: { new: 1, approved: 0, rejected: 1 } },
];

const queued = (id, postSlug, postTitle, name, status, content) => ({
  postSlug,
  postTitle,
  comment: {
    id,
    createdDate: '2026-02-04T00:00:00Z',
    commenter: { id: `${name.toLowerCase()}-id`, email: `${name.toLowerCase()}@example.com`, name, status, createdDate: '2026-01-01T00:00:00Z' },
    status: 'new',
    content,
  },
});

export const queue = [
  queued('c-1', 'first', 'First', 'Ada', 'new', 'The first comment.'),
  queued('c-2', 'first', 'First', 'Grace', 'trusted', 'The second comment.'),
  queued('c-3', 'second', 'Second', 'Alan', 'new', 'The third comment.'),
];

export const answer = (status, body) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

export const postsAnswer = () => answer(200, { success: true, message: null, data: posts });

export const queueAnswer = () => answer(200, { success: true, message: null, data: queue });

export const siteDeploy = {
  deploy: 'deployed',
  production: { number: 40, state: 'succeeded', url: 'https://github.com/tw0po1nt/twopoint-website/actions/runs/40' },
  staging: null,
};

export const siteDeployAnswer = () => answer(200, { success: true, message: null, data: siteDeploy });

// The default API answers the three calls of the start: the posts, the moderation queue, and the deploy state of the site.
export const apiAnswer = (url) => {
  if (url.endsWith('/api/internal/comments')) {
    return queueAnswer();
  }

  if (url.endsWith('/api/internal/deploys')) {
    return siteDeployAnswer();
  }

  return postsAnswer();
};

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
      account: () => 'Ada Lovelace',
      signOut: async () => {
        events.push('signOut');
      },
      ...members,
    },
  };
};

// The stub push has a public key, no subscription, and the permission "default". A test replaces a member to change that.
export const stubPush = (members = {}) => {
  const events = [];

  return {
    events,
    port: {
      support: () => 'supported',
      permission: () => 'default',
      publicKey: () => 'BJfO8M7cCATrska2CFKeyAYsUaYU5kOMsHea2Evge-a5F1Vu9FGp3XxlGX9MjbssjX9QCV1DsaNj0B-ExteCxk4',
      subscription: async () => {
        events.push('subscription');
        return undefined;
      },
      subscribe: async () => {
        events.push('subscribe');
        return undefined;
      },
      unsubscribe: async () => {
        events.push('unsubscribe');
      },
      onSubscriptionChange: () => {
        events.push('onSubscriptionChange');
      },
      onNewComment: () => {
        events.push('onNewComment');
      },
      onOpenRequest: () => {
        events.push('onOpenRequest');
      },
      setBadge: (count) => {
        events.push(`setBadge:${count}`);
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
export const load = async (url, { signIn = stubSignIn(), push = stubPush(), respond = apiAnswer } = {}) => {
  const manifest = JSON.parse(await readFile('build/manifest.json', 'utf8'));
  const config = JSON.parse(await readFile('config.json', 'utf8'))[manifest.mode];

  GlobalRegistrator.register({ url });
  document.body.innerHTML = '<div id="admin"><noscript>The admin needs JavaScript.</noscript></div>';

  const calls = [];
  globalThis.fetch = async (input, init = {}) => {
    calls.push({ url: String(input), authorization: new Headers(init.headers).get('Authorization') });
    return respond(String(input), init);
  };

  const app = await import(pathToFileURL(path.resolve('dist', manifest.app.slice(1))).href);
  app.start(signIn.port, push.port);
  await settle();
  return { calls, config, events: signIn.events, pushEvents: push.events };
};

export const heading = () => document.querySelector('main h1').textContent.trim();

export const banner = () => document.querySelector('main .banner')?.textContent.trim() ?? null;

export const summary = () => [...document.querySelectorAll('main .stat')].map((item) => item.textContent.trim());

// A link in the navigation can have a count after its label.
const label = (element) => (element.querySelector('.nav-label') ?? element).textContent.trim();

const navigation = (selector) =>
  [...document.querySelectorAll(`${selector} a, ${selector} button`)].map((item) => ({
    text: label(item),
    href: item.getAttribute('href'),
    current: item.getAttribute('aria-current'),
  }));

export const links = () => navigation('nav.side-nav');

export const tabs = () => navigation('nav.tab-bar');

export const count = (selector) => document.querySelector(`${selector} .badge.count`)?.textContent.trim() ?? null;

export const follow = (text, within = document) => {
  const link = [...within.querySelectorAll('a')].find((item) => label(item) === text);
  link.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, button: 0 }));
};

export const press = (text, within = document) => {
  const button = [...within.querySelectorAll('button')].find((item) => item.textContent.trim() === text);
  button.click();
};

// happy-dom opens a dialog with showModal as a plain open dialog, so the spy records each call.
export const spyOnShowModal = () => {
  const calls = [];
  const showModal = HTMLDialogElement.prototype.showModal;

  HTMLDialogElement.prototype.showModal = function () {
    calls.push(this.id);
    showModal.call(this);
  };

  return calls;
};

// happy-dom has no close request at the Esc key, so the helper sends the cancel and the close as a browser does.
export const pressEscape = (dialog) => {
  const cancel = new Event('cancel', { cancelable: true });
  dialog.dispatchEvent(cancel);

  if (!cancel.defaultPrevented) {
    dialog.close();
  }
};
