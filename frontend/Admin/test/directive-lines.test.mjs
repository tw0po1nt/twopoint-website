import { parser } from '@lezer/markdown';
import assert from 'node:assert/strict';
import { test } from 'node:test';

import { directiveLines } from '../directive-lines.js';

const markdown = parser.configure(directiveLines);

const directives = (text) => {
  const found = [];
  markdown.parse(text).iterate({
    enter: (node) => {
      if (node.name === 'DirectiveLine') {
        found.push(text.slice(node.from, node.to));
      }
    },
  });
  return found;
};

test('a leaf directive alone on its line is a directive line', () => {
  const text = 'Before.\n\n::youtube{id="qRuKTkDvAk4"}\n\nAfter.\n\n::pdf{file="https://example.com/a.pdf"}\n';
  assert.deepEqual(directives(text), ['::youtube{id="qRuKTkDvAk4"}', '::pdf{file="https://example.com/a.pdf"}']);
});

test('text that only looks like a directive is not a directive line', () => {
  assert.deepEqual(directives('A sentence with ::youtube{id="x"} inside.\n\n::: not a directive\n\n::name without braces\n'), []);
});

test('a directive line inside a code fence is code and gives no directive', () => {
  assert.deepEqual(directives('```\n::youtube{id="x"}\n```\n'), []);
});
