# Welcome to the twopoint.dev website 👋

![Version](https://img.shields.io/badge/version-1.0.0-blue.svg?cacheSeconds=2592000)

> This is the repository containing the code for twopoint.dev, a technical blog by Matthew Watt.

<br>

# Overview

This website is a static site. An F# generator in `frontend/` builds the site from the Markdown posts in `frontend/content/posts/`. Fable compiles the interactive widgets to JavaScript. `backend/` is an F# Azure Functions app for comments, newsletter subscriptions, and notifications.
<br />

## Getting started

### 1. Clone the repository

```sh
git clone https://github.com/tw0po1nt/twopoint-website.git
```

### 2. Build the widgets

```sh
cd frontend/Widgets
npm ci
npm run build
```

### 3. Build the site

Run this command from the root of the repository:

```sh
dotnet run --project frontend/Generator -- build --stage local
```

The site is in `frontend/dist/website/`.

## Live site

[View live site](https://twopoint.dev)

## Author

👤 **Matthew Watt**

-   [GitHub](https://github.com/tw0po1nt)
