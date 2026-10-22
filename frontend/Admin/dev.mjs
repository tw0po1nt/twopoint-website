import { spawn } from 'node:child_process';
import { readFile } from 'node:fs/promises';

import { serve } from './server.mjs';

const config = JSON.parse(await readFile('staticwebapp.config.json', 'utf8'));
const { origin } = JSON.parse(await readFile('config.json', 'utf8')).dev;

await serve({ root: 'dist', config, port: Number(new URL(origin).port) });
console.log(`dev.mjs: serves dist/ on ${origin}`);

const fable = spawn(
  'dotnet',
  ['fable', 'watch', 'Admin.fsproj', '--outDir', 'build/fable', '--extension', '.jsx', '--exclude', 'Oxpecker.Solid.FablePlugin', '--sourceMaps', '--silent', '--runWatch', 'node', 'build.mjs', '--dev'],
  { stdio: 'inherit' },
);

fable.on('exit', (code) => process.exit(code ?? 1));
