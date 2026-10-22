import { transformAsync } from '@babel/core';
import solid from 'babel-preset-solid';
import * as esbuild from 'esbuild';
import { copyFile, mkdir, readFile, readdir, rm, writeFile } from 'node:fs/promises';
import path from 'node:path';

const fableDir = 'build/fable';
const outDir = 'dist';
const bundleDir = path.join(outDir, 'bundle');
const dev = process.argv.includes('--dev');
const mode = dev ? 'dev' : 'release';
const sourcemap = dev ? 'linked' : false;

const fail = (message) => {
  console.error(`build.mjs: ${message}`);
  process.exit(1);
};

// esbuild cannot compile Solid JSX, so babel-preset-solid compiles the JSX that Fable writes.
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
        sourceMaps: dev ? 'inline' : false,
      });

      return { contents: result.code, loader: 'js' };
    });
  },
};

const config = JSON.parse(await readFile('config.json', 'utf8'))[mode];
const define = Object.fromEntries(Object.entries(config).map(([key, value]) => [`ADMIN_CONFIG.${key}`, JSON.stringify(value)]));

await mkdir(path.join(outDir, 'redirect'), { recursive: true });

const result = await esbuild.build({
  entryPoints: { admin: path.join(fableDir, 'Main.jsx'), style: 'admin.css' },
  outdir: bundleDir,
  entryNames: '[name]-[hash]',
  bundle: true,
  format: 'esm',
  minify: true,
  target: 'es2022',
  sourcemap,
  metafile: true,
  logLevel: 'warning',
  define,
  plugins: [solidJsx],
});

const outputs = Object.keys(result.metafile.outputs);
const output = (extension) => `/${path.relative(outDir, outputs.find((file) => file.endsWith(extension)))}`;
const files = { script: output('.js'), style: output('.css') };

const template = await readFile('index.html', 'utf8');

for (const placeholder of ['/bundle/admin.js', '/bundle/admin.css']) {
  if (!template.includes(`"${placeholder}"`)) {
    fail(`index.html has no reference to ${placeholder}.`);
  }
}

const page = template.replace('"/bundle/admin.js"', `"${files.script}"`).replace('"/bundle/admin.css"', `"${files.style}"`);

await writeFile(path.join(outDir, 'index.html'), page);
await copyFile('redirect/index.html', path.join(outDir, 'redirect', 'index.html'));
await copyFile('staticwebapp.config.json', path.join(outDir, 'staticwebapp.config.json'));

// The dev server serves this folder, so the build writes each new file first and deletes the old files last.
const kept = new Set(outputs.map((file) => path.basename(file)));

for (const file of await readdir(bundleDir)) {
  if (!kept.has(file)) {
    await rm(path.join(bundleDir, file), { force: true });
  }
}

await writeFile('build/manifest.json', `${JSON.stringify({ mode, ...files }, null, 2)}\n`);

console.log(`build.mjs: wrote ${outDir}/ in the ${mode} mode (${files.script}, ${files.style})`);
