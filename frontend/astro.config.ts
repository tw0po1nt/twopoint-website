import path from 'path';
import { fileURLToPath } from 'url';

import { defineConfig } from 'astro/config';
import react from '@astrojs/react';

import sitemap from '@astrojs/sitemap';
import tailwind from '@astrojs/tailwind';
import mdx from '@astrojs/mdx';
import partytown from '@astrojs/partytown';
import icon from 'astro-icon';
import compress from 'astro-compress';
import type { AstroIntegration } from 'astro';
import fable from 'vite-plugin-fable';

import astrowind from './vendor/integration';

import { readingTimeRemarkPlugin, responsiveTablesRehypePlugin, lazyImagesRehypePlugin } from './src/utils/frontmatter';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const fsproj = path.join(__dirname, 'src/src.fsproj');
const stage = process.env.PUBLIC_STAGE;

const hasExternalScripts = true;
const whenExternalScripts = (items: (() => AstroIntegration) | (() => AstroIntegration)[] = []) =>
  hasExternalScripts ? (Array.isArray(items) ? items.map((item) => item()) : [items()]) : [];

function fsharpMiddlewarePlugin() {
  return {
    name: "twopoint",
    configureServer(server) {
      server.middlewares.use((req, res, next) => {
        const isFSharpUrl = /\.fs/.test(req.url);

        if (isFSharpUrl && req.url.indexOf("?import") === -1) {
          req.url += "?import";
          res.setHeader("Content-Type", "application/javascript");
        }
        return next();
      });
    },
    handleHotUpdate: async function ({ file, server, modules }) {
      if (/\.fs/.test(file) && modules && modules.length === 0) {
        const module = server.moduleGraph.getModuleById(file);
        if (module) {
          server.ws.send({
            type: "custom",
            event: "hot-update-dependents",
            data: [module.url],
          });
          return [module];
        }
      }
    },
  };
}

export default defineConfig({
  output: 'static',

  integrations: [
    react({ include: /\.(fs|js|jsx|ts|tsx)$/ }),
    tailwind({
      applyBaseStyles: false,
    }),
    sitemap(),
    mdx(),
    icon({
      include: {
        tabler: ['*'],
        'flat-color-icons': [
          'template',
          'gallery',
          'approval',
          'document',
          'advertising',
          'currency-exchange',
          'voice-presentation',
          'business-contact',
          'database',
        ],
      },
    }),

    ...whenExternalScripts(() =>
      partytown({
        config: { forward: ['dataLayer.push'] },
      })
    ),

    compress({
      CSS: true,
      HTML: {
        'html-minifier-terser': {
          removeAttributeQuotes: false,
        },
      },
      Image: false,
      JavaScript: true,
      SVG: false,
      Logger: 1,
    }),

    astrowind({
      config: stage === 'staging' ? './src/staging.config.yaml' : './src/config.yaml',
    }),
  ],

  image: {
    domains: ['cdn.pixabay.com', 'unsplash.com', 'twopointwebsite.blob.core.windows.net', 'img.youtube.com'],
  },

  markdown: {
    syntaxHighlight: 'shiki',
    shikiConfig: {
      theme: 'github-dark',
      wrap: true,
    },
    remarkPlugins: [readingTimeRemarkPlugin],
    rehypePlugins: [responsiveTablesRehypePlugin, lazyImagesRehypePlugin],
  },

  vite: {
    resolve: {
      alias: {
        '~': path.resolve(__dirname, './src'),
      },
    },
    plugins: [fsharpMiddlewarePlugin(), fable({ fsproj, jsx: 'automatic' })]
  },
});
