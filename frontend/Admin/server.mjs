import { readFile, stat } from 'node:fs/promises';
import { createServer } from 'node:http';
import path from 'node:path';

const types = {
  '.css': 'text/css; charset=utf-8',
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.json': 'application/json',
  '.map': 'application/json',
};

const escapeRegExp = (text) => text.replace(/[.+?^${}()|[\]\\]/g, '\\$&');

// A route pattern of Azure Static Web Apps, where `*` matches any text.
const matches = (pattern, pathname) => new RegExp(`^${pattern.split('*').map(escapeRegExp).join('.*')}$`).test(pathname);

const decode = (url) => {
  try {
    return decodeURIComponent(new URL(url, 'http://localhost').pathname);
  } catch {
    return null;
  }
};

const kind = async (file) => {
  try {
    const info = await stat(file);
    return info.isDirectory() ? 'directory' : 'file';
  } catch {
    return 'missing';
  }
};

export const serve = ({ root, config, port }) => {
  const base = path.resolve(root);
  const fallback = config.navigationFallback;

  const headers = (pathname) => ({
    ...config.globalHeaders,
    ...config.routes.find((route) => matches(route.route, pathname))?.headers,
  });

  const send = async (response, pathname, file, status = 200) => {
    const body = await readFile(file);
    response.writeHead(status, { ...headers(pathname), 'Content-Type': types[path.extname(file)] ?? 'application/octet-stream' });
    response.end(body);
  };

  const server = createServer(async (request, response) => {
    const pathname = decode(request.url);
    const file = pathname === null ? base : path.join(base, pathname);

    if (pathname === null || (file !== base && !file.startsWith(`${base}${path.sep}`))) {
      response.writeHead(400, config.globalHeaders);
      response.end();
      return;
    }

    const found = await kind(file);

    if (found === 'directory' && !pathname.endsWith('/')) {
      response.writeHead(301, { ...headers(pathname), Location: `${pathname}/` });
      response.end();
    } else if (found === 'directory' && (await kind(path.join(file, 'index.html'))) === 'file') {
      await send(response, pathname, path.join(file, 'index.html'));
    } else if (found === 'file') {
      await send(response, pathname, file);
    } else if (!fallback.exclude.some((pattern) => matches(pattern, pathname))) {
      const page = path.join(base, fallback.rewrite);

      if ((await kind(page)) === 'file') {
        await send(response, pathname, page);
      } else {
        response.writeHead(503, { ...headers(pathname), 'Content-Type': 'text/plain; charset=utf-8' });
        response.end('The first build is not ready');
      }
    } else {
      response.writeHead(404, { ...headers(pathname), 'Content-Type': 'text/plain; charset=utf-8' });
      response.end('Not found');
    }
  });

  return new Promise((resolve) => server.listen(port, 'localhost', () => resolve(server)));
};
