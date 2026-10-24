import { transformAsync } from '@babel/core';
import solid from 'babel-preset-solid';
import * as esbuild from 'esbuild';
import { copyFile, cp, mkdir, readFile, readdir, rm, writeFile } from 'node:fs/promises';
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

// `admin` starts the app with MSAL. The bundle test starts `app` with a stub sign-in.
// In the dev mode, `reload` is the reload widget of the site. It reloads the page when the dev host sees a new build.
const entryPoints = {
  admin: path.join(fableDir, 'Main.jsx'),
  app: path.join(fableDir, 'App.jsx'),
  bridge: path.join(fableDir, 'Bridge.jsx'),
  style: 'admin.css',
  ...(dev ? { reload: path.join(fableDir, 'Widgets', 'DevReload.jsx') } : {}),
};

const result = await esbuild.build({
  entryPoints,
  outdir: bundleDir,
  entryNames: '[name]-[hash]',
  bundle: true,
  splitting: true,
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
const output = (entry) => {
  const file = outputs.find((item) => result.metafile.outputs[item].entryPoint === entryPoints[entry]);
  return `/${path.relative(outDir, file)}`;
};
const files = {
  script: output('admin'),
  app: output('app'),
  bridge: output('bridge'),
  style: output('style'),
  ...(dev ? { reload: output('reload') } : {}),
};

// Each page names its bundle files with these placeholders, and the build writes the hashed names.
const page = async (template, replacements) => {
  let text = await readFile(template, 'utf8');

  for (const [placeholder, file] of Object.entries(replacements)) {
    if (!text.includes(`"${placeholder}"`)) {
      fail(`${template} has no reference to ${placeholder}.`);
    }

    text = text.replace(`"${placeholder}"`, `"${file}"`);
  }

  return text;
};

const index = await page('index.html', { '/bundle/admin.js': files.script, '/bundle/admin.css': files.style });
const reloadTag = dev ? `  <script type="module" src="${files.reload}"></script>\n  ` : '';

await writeFile(path.join(outDir, 'index.html'), index.replace('</head>', `${reloadTag}</head>`));
await writeFile(path.join(outDir, 'redirect', 'index.html'), await page('redirect/index.html', { '/bundle/bridge.js': files.bridge }));
await copyFile('staticwebapp.config.json', path.join(outDir, 'staticwebapp.config.json'));

// The push worker, the manifest, and the icons keep their names, because the browser finds them at fixed paths.
await cp('public', outDir, { recursive: true });

// The dev server serves this folder, so the build writes each new file first and deletes the old files last.
const kept = new Set(outputs.map((file) => path.basename(file)));

for (const file of await readdir(bundleDir)) {
  if (!kept.has(file)) {
    await rm(path.join(bundleDir, file), { force: true });
  }
}

await writeFile('build/manifest.json', `${JSON.stringify({ mode, ...files }, null, 2)}\n`);

console.log(`build.mjs: wrote ${outDir}/ in the ${mode} mode (${files.script}, ${files.bridge}, ${files.style})`);
