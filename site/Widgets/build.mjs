import { transformAsync } from '@babel/core';
import solid from 'babel-preset-solid';
import * as esbuild from 'esbuild';
import { readFile, readdir, rm, writeFile } from 'node:fs/promises';
import path from 'node:path';

const fableDir = 'build/fable';
const entriesDir = path.join(fableDir, 'Entries');
const outDir = 'dist';
const headLimit = 2048;

const fail = (messages) => {
  for (const message of messages) {
    console.error(`build.mjs: ${message}`);
  }

  process.exit(1);
};

// Fable writes the Solid views as JSX, and babel-preset-solid compiles that JSX to DOM calls.
const solidJsx = {
  name: 'solid-jsx',
  setup(build) {
    build.onLoad({ filter: /\.jsx$/ }, async (args) => {
      const source = await readFile(args.path, 'utf8');

      const result = await transformAsync(source, {
        filename: args.path,
        presets: [solid],
        babelrc: false,
        configFile: false,
      });

      return { contents: result.code, loader: 'js' };
    });
  },
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

// Each file of Entries/ is the module entry of one widget: Entries/Subscribe.fs gives the entry `subscribe`.
const widgetEntries = async () => {
  const files = (await readdir(entriesDir)).filter((file) => file.endsWith('.jsx')).sort();

  return Object.fromEntries(files.map((file) => [path.basename(file, '.jsx').toLowerCase(), path.join(entriesDir, file)]));
};

// Gives each chunk that `file` imports, also through another chunk, so that a page can preload each one.
const staticImports = (outputs, file) => {
  const found = new Set();

  const visit = (current) => {
    for (const item of outputs[current].imports) {
      if (item.kind === 'import-statement' && !item.external && !found.has(item.path)) {
        found.add(item.path);
        visit(item.path);
      }
    }
  };

  visit(file);
  return [...found].map((item) => path.relative(outDir, item)).sort();
};

const modules = async (entries) => {
  const result = await esbuild.build({
    entryPoints: entries,
    outdir: outDir,
    entryNames: '[name]-[hash]',
    chunkNames: 'chunk-[hash]',
    bundle: true,
    splitting: true,
    format: 'esm',
    minify: true,
    target: 'es2022',
    metafile: true,
    logLevel: 'warning',
    plugins: [solidJsx],
  });

  const outputs = result.metafile.outputs;
  const names = Object.fromEntries(Object.entries(entries).map(([name, entry]) => [entry, name]));

  return Object.fromEntries(
    Object.entries(outputs)
      .filter(([, output]) => names[output.entryPoint])
      .map(([file, output]) => [names[output.entryPoint], { file: path.relative(outDir, file), imports: staticImports(outputs, file) }])
      .sort(([a], [b]) => a.localeCompare(b)),
  );
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

const entries = await modules(await widgetEntries());

const manifest = { entries, head: head.file, devReload: devReload.file };
await writeFile(path.join(outDir, 'manifest.json'), `${JSON.stringify(manifest, null, 2)}\n`);

console.log(`build.mjs: wrote ${outDir}/ (head script ${head.bytes} bytes, entries: ${Object.keys(entries).join(', ')})`);
