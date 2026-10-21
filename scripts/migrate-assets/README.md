# Move the old assets

This one-time procedure moves each image and PDF of the posts into the public `assets` container
on `twopointwebsite`. The procedure also changes the URLs in the posts. The clean-up deletes this
folder.

`migrate-assets.fsx` downloads each asset from its old URL with the request of the build image
fetch. It then writes each asset to `assets` as `<first 16 hex characters of the SHA-256>.<ext>`.
It does not change the bytes. `url-map.json` contains the old URL and the new URL of each asset.

## Checklist

1. Sign in to Azure:

   ```sh
   az login
   ```

2. Make sure that your account has the `Storage Blob Data Contributor` role on `twopointwebsite`.
3. Make the `assets` container. The `blob` access level gives public read access to each blob, and
   no list access:

   ```sh
   az storage container create --name assets --account-name twopointwebsite \
     --public-access blob --auth-mode login
   ```

4. Make sure that the access level of the container is `blob`:

   ```sh
   az storage container show --name assets --account-name twopointwebsite \
     --auth-mode login --query properties.publicAccess
   ```

5. From the repo root, run the script with no write, and examine the list of assets:

   ```sh
   dotnet fsi scripts/migrate-assets/migrate-assets.fsx --dry-run
   ```

6. Run the script:

   ```sh
   dotnet fsi scripts/migrate-assets/migrate-assets.fsx
   ```

   The script writes each blob, writes `url-map.json`, and changes the URLs in the posts. A blob
   that is already in the container stays as it is, so you can run the script again after a
   failure.

7. Make sure that a request to list the container fails:

   ```sh
   curl -s -o /dev/null -w '%{http_code}\n' \
     'https://twopointwebsite.blob.core.windows.net/assets?restype=container&comp=list'
   ```

   The result is `404` or `403`.

## The check that the site stays the same

1. Build the website and the newsletters from `main`, and copy `frontend/dist/` to a temporary
   folder.
2. Apply the URL map to that copy:

   ```sh
   dotnet fsi scripts/migrate-assets/migrate-assets.fsx apply-map <folder>
   ```

3. Build the website and the newsletters from the branch.
4. Compare the two folders with `diff -r`, and put the result in the PR body.

Run the two builds one after the other, when no post is upcoming.
