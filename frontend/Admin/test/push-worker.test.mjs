import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';
import vm from 'node:vm';

const source = await readFile('dist/sw.js', 'utf8');

// The worker runs in a scope with `self`, so the test gives it a stub of that scope.
const start = ({ pages = [] } = {}) => {
  const listeners = {};
  const subscribed = [];
  const messages = [];

  const self = {
    addEventListener: (type, listener) => {
      listeners[type] = listener;
    },
    registration: {
      pushManager: {
        subscribe: async (options) => {
          subscribed.push(options);
        },
      },
    },
    clients: {
      matchAll: async () => pages.map((page) => ({ postMessage: (message) => messages.push([page, message]) })),
      claim: async () => {},
    },
    skipWaiting: () => {},
  };

  vm.runInNewContext(source, { self });
  return { listeners, subscribed, messages };
};

// The worker runs in another JavaScript global scope, so the test compares plain values.
const types = (worker) => worker.messages.map(([page, message]) => [page, message.type]);

const fire = async (worker, type, event) => {
  const waits = [];
  worker.listeners[type]({ ...event, waitUntil: (promise) => waits.push(promise) });
  await Promise.all(waits);
};

test('the worker listens for pushsubscriptionchange', () => {
  assert.equal(typeof start().listeners.pushsubscriptionchange, 'function');
});

test('after a pushsubscriptionchange event, the worker subscribes again with the old key and tells each open page', async () => {
  const worker = start({ pages: ['one', 'two'] });
  const applicationServerKey = new Uint8Array([4, 1, 2]);

  await fire(worker, 'pushsubscriptionchange', { oldSubscription: { options: { applicationServerKey } } });

  assert.equal(worker.subscribed.length, 1);
  assert.equal(worker.subscribed[0].userVisibleOnly, true);
  assert.equal(worker.subscribed[0].applicationServerKey, applicationServerKey);
  assert.deepEqual(types(worker), [['one', 'pushsubscriptionchange'], ['two', 'pushsubscriptionchange']]);
});

test('a pushsubscriptionchange event with a new subscription does not subscribe again, and tells the open pages', async () => {
  const worker = start({ pages: ['one'] });
  const applicationServerKey = new Uint8Array([4, 1, 2]);

  await fire(worker, 'pushsubscriptionchange', {
    newSubscription: {},
    oldSubscription: { options: { applicationServerKey } },
  });

  assert.equal(worker.subscribed.length, 0);
  assert.deepEqual(types(worker), [['one', 'pushsubscriptionchange']]);
});

test('a pushsubscriptionchange event with a null old subscription does not subscribe, and tells the open pages', async () => {
  const worker = start({ pages: ['one'] });

  await fire(worker, 'pushsubscriptionchange', { oldSubscription: null, newSubscription: null });

  assert.equal(worker.subscribed.length, 0);
  assert.deepEqual(types(worker), [['one', 'pushsubscriptionchange']]);
});

test('a pushsubscriptionchange event with no field does not subscribe, and tells the open pages', async () => {
  const worker = start({ pages: ['one'] });

  await fire(worker, 'pushsubscriptionchange', {});

  assert.equal(worker.subscribed.length, 0);
  assert.deepEqual(types(worker), [['one', 'pushsubscriptionchange']]);
});
