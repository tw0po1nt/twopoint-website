// This file must run on an old Node, so it uses no new syntax and no package.
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export const problems = ({ pin, engines, version }) => {
  if (!/^\d+$/.test(pin)) {
    return [`.node-version must contain one Node major version, such as 26. It contains "${pin}".`];
  }

  const found = [];
  const range = `>=${pin}`;

  if (engines !== range) {
    found.push(
      `engines.node in package.json is ${JSON.stringify(engines)}, but .node-version pins Node ${pin}. ` +
        `Set engines.node to "${range}".`,
    );
  }

  const major = Number(version.replace(/^v/, '').split('.')[0]);

  if (major < Number(pin)) {
    found.push(
      `This package needs Node ${pin} or newer (.node-version), but this shell runs Node ${version}. ` +
        `Run "nvm use" in this package to select Node ${pin}, then run the command again.`,
    );
  }

  return found;
};

export const check = (packageDir, version) => {
  const pin = readFileSync(path.join(packageDir, '.node-version'), 'utf8').trim();
  const manifest = JSON.parse(readFileSync(path.join(packageDir, 'package.json'), 'utf8'));
  const engines = manifest.engines && manifest.engines.node;
  return problems({ pin, engines, version });
};

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const found = check(process.cwd(), process.version);

  for (const problem of found) {
    console.error(`check-node: ${problem}`);
  }

  if (found.length > 0) {
    process.exit(1);
  }
}
