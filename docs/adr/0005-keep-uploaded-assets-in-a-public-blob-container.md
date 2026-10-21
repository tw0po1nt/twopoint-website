# Keep uploaded assets in a public Blob container

The posts are in a private Blob store, so a draft stays private. The assets of a post are images
and PDFs. A reader's browser and an email client load each body image and each PDF from its URL.
The build gets each image over HTTP for its size and its `og:image` crop.

We keep each asset in a public `assets` container on `twopointwebsite`, the account that serves
the images today. The admin uploads a file through a backend endpoint. The backend writes the blob
with a name from the SHA-256 of the file, and returns the plain Blob URL. The Markdown contains
that URL. An asset is public at its upload, before the publish of its post. Nobody can guess the
name without the file, and the container cannot be listed.

## Considered options

- **Assets in the private post store, copied into the site.** A draft image stays private. The
  Markdown then has a reference that is not a URL. The build, the backend preview, and the
  newsletter must change it into a URL, and the build needs read access to all the assets.
- **A private container, copied to a public container at the publish.** A draft image stays
  private, and the site keeps Blob URLs. The publish then changes the text of the author.
- **A custom domain through Azure Front Door.** Shorter URLs, and the store can move with no
  change to the posts. Front Door Standard costs approximately USD 36 each month.

## Consequences

- An image in a draft is public at its upload.
- The blob name comes from the bytes, so a new draft needs no slug before an upload, and a blob
  never changes.
- The backend does not delete an asset. A revision and a sent newsletter keep their images.
- A move to a custom domain is a rewrite of the URLs in the post store.

Detail: [Choose where uploaded assets live](https://github.com/tw0po1nt/twopoint-website-staging/issues/201).
