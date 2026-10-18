import * as esbuild from 'esbuild';
import { rm, writeFile } from 'node:fs/promises';
import path from 'node:path';

const fableDir = 'build/fable';
const outDir = 'dist';
const headLimit = 2048;

const fail = (messages) => {
  for (const message of messages) {
    console.error(`build.mjs: ${message}`);
  }

  process.exit(1);
};

// A classic IIFE has no `import` or `export`, so a page can run it inline while the parser waits.
const iife = async (name, entry) => {
  const result = await esbuild.build({
    entryPoints: { [name]: path.join(fableDir, entry) },
    outdir: outDir,
    entryNames: '[name]-[hash]',
    bundle: true,
    format: 'iife',
    minify: true,
    target: 'es2022',
    metafile: true,
    logLevel: 'warning',
  });

  const [[file, output]] = Object.entries(result.metafile.outputs);
  return { file: path.relative(outDir, file), bytes: output.bytes, inputs: Object.keys(output.inputs) };
};

await rm(outDir, { recursive: true, force: true });

const head = await iife('head', 'HeadMain.jsx');

const library = head.inputs.filter((input) => input.includes('fable-library'));

const headErrors = [
  ...(head.bytes > headLimit ? [`the head script is ${head.bytes} bytes. The limit is ${headLimit} bytes.`] : []),
  ...(library.length > 0 ? [`the head script contains fable-library code: ${library.join(', ')}`] : []),
];

if (headErrors.length > 0) {
  fail(headErrors);
}

const devReload = await iife('dev-reload', 'DevReload.jsx');

const manifest = { entries: {}, head: head.file, devReload: devReload.file };
await writeFile(path.join(outDir, 'manifest.json'), `${JSON.stringify(manifest, null, 2)}\n`);

console.log(`build.mjs: wrote ${outDir}/ (head script ${head.bytes} bytes)`);
