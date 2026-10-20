# Welcome to the twopoint.dev website 👋

![Version](https://img.shields.io/badge/version-2.0.0-blue.svg?cacheSeconds=2592000)

> This is the repository containing the code for twopoint.dev, a technical blog by Matthew Watt.

<br>

# Overview

This website is a static site. An F# generator in `frontend/` builds the site from the Markdown posts in `frontend/content/posts/`. Fable compiles the interactive widgets to JavaScript. `backend/` is an F# Azure Functions app for comments, newsletter subscriptions, and notifications.
<br />

```text
frontend/
├── content/      # posts, privacy page, assets, styles, email CSS
├── Generator/    # builds the site and the newsletters, and uploads the newsletters
├── Widgets/      # comments, subscribe, unsubscribe, and PDF viewer, in Fable
└── Dev/          # dev server with live reload
backend/          # Azure Functions app
```

## Getting started

### 1. Clone the repository

```sh
git clone https://github.com/tw0po1nt/twopoint-website.git
```

### 2. Install the tools

- The .NET SDK in `global.json`
- The Node.js version in `frontend/Widgets/.node-version`

Then restore the .NET tools (Fable and SageFs) from the root of the repository:

```sh
dotnet tool restore
```

### 3. Install the widget packages

```sh
cd frontend/Widgets
npm ci
```

### 4. Run the site locally

Use one of these two paths. Each path gives the site at [http://localhost:4321](http://localhost:4321).

#### Path A: the dev server

Use this path to write posts or change the site. The dev server renders each page in memory, so it needs no build first.

Start a SageFs session in `frontend/Dev/`. The init script starts the dev server and the widget watch. A save of a post, a style, or a widget reloads the page.

On the first start, the page shows an error until the widget watch writes `frontend/Widgets/dist/`. Then the page reloads.

#### Path B: the static build

Use this path to see the files that a deploy sends to GitHub Pages.

1. Build the widgets in `frontend/Widgets/`:

   ```sh
   npm run build
   ```

2. Build the site from the root of the repository:

   ```sh
   dotnet run --project frontend/Generator -- build --stage local
   ```

3. Serve the site with a static file server:

   ```sh
   python3 -m http.server 4321 --directory frontend/dist/website
   ```

The build writes the site to `frontend/dist/website/` and the newsletters to `frontend/dist/newsletter/`.

## Tests

```sh
scripts/verify.sh
```

The script runs the prose check, builds the solution, and runs the .NET tests. To run the widget tests, run `npm test` in `frontend/Widgets/`.

## Deploys

A push to `main` builds the site and deploys it to GitHub Pages. In the production repository, the workflow also uploads the newsletters to Azure Blob storage before the deploy.

## Live site

[View live site](https://twopoint.dev)

## Author

👤 **Matthew Watt**

-   [GitHub](https://github.com/tw0po1nt)
