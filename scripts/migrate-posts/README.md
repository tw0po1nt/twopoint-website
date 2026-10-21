# Move the posts into the post store

This one-time procedure is migration step 2 of the web admin. It sets up the private post store,
and then uploads the posts and the series records from Git. The clean-up deletes this folder.

`wizard.sh` makes these Azure resources:

- The storage account (`twopointposts` by default), in the resource group and the region of
  `twopointwebsite`. Blob public access and shared key access are off for the whole account.
- The four containers: `published`, `working`, `revisions`, and `series`.
- `Storage Blob Data Contributor` on the account for your Azure user. With it, you can run `pull` on
  the local stage, run the upload script, and upload a post by hand until the admin is live.
- The build identity, a user-assigned managed identity (`id-twopoint-site-build` by default). It
  gets `Storage Blob Data Reader` on `published` and `series` only.
- Four federated credentials on the build identity, two for each site repo:
  `repo:<repo>:pull_request` for the CI `site` job, and `repo:<repo>:ref:refs/heads/main` for a
  `workflow_dispatch` run of `site.yml` and a push to `main`.
- In `tw0po1nt/twopoint-website-staging` and `tw0po1nt/twopoint-website`: the secret
  `AZURE_BUILD_CLIENT_ID`, the variable `POST_STORE_ACCOUNT`, and the secrets `AZURE_TENANT_ID` and
  `AZURE_SUBSCRIPTION_ID` if a repo does not have them.

`migrate-posts.fsx` writes these blobs:

| Container | Blob | Contents | Metadata |
| --- | --- | --- | --- |
| `published` | `<slug>.md` | The post file from Git | `migrated=true` |
| `revisions` | `<slug>/<publishDate>.md` | The same bytes as the `published` blob | None |
| `series` | `<slug>.yml` | The series record from Git | None |

The revision name has the `publishDate` of the front matter in the round-trip format, for example
`2025-04-11T13:00:00.0000000Z`. Each write sends `If-None-Match: *`. A blob that is already in the
store stays as it is, so a second run changes no blob.

## Checklist

Do not change a post from step 6 until the release of migration step 2 to `prod`.

1. Install the Azure CLI and the GitHub CLI.
2. Sign in to GitHub:

   ```sh
   gh auth login
   ```

3. From the repo root, run the wizard:

   ```sh
   scripts/migrate-posts/wizard.sh
   ```

   The wizard asks before each change. You can run it again after a failure, because it skips each
   resource that exists.

4. Wait 10 minutes, so that the new roles take effect.
5. Run the script with `--dry-run`. It writes no blob. Examine the list of blobs:

   ```sh
   dotnet fsi scripts/migrate-posts/migrate-posts.fsx --account twopointposts --dry-run
   ```

6. Run the script:

   ```sh
   dotnet fsi scripts/migrate-posts/migrate-posts.fsx --account twopointposts
   ```

   The script writes 22 blobs: 10 posts, 10 revisions, and 2 series records.

7. Make sure that each `published` blob has the migrated mark:

   ```sh
   az storage blob list --account-name twopointposts --auth-mode login -c published \
     --include m --query "[].{name:name, migrated:metadata.migrated}" -o table
   ```

8. Make sure that `revisions` contains 10 blobs and `series` contains 2 blobs:

   ```sh
   az storage blob list --account-name twopointposts --auth-mode login -c revisions \
     --query "length(@)"
   az storage blob list --account-name twopointposts --auth-mode login -c series \
     --query "length(@)"
   ```

## A run against Azurite

The `--azurite` option uses the Azurite connection string `UseDevelopmentStorage=true`. Azurite
starts with no container, so the script makes the four containers first.

1. Start Azurite in an empty folder:

   ```sh
   npx azurite@3.35.0 --blobHost 127.0.0.1 --location <folder> --silent --skipApiVersionCheck
   ```

2. From the repo root, run the script two times:

   ```sh
   dotnet fsi scripts/migrate-posts/migrate-posts.fsx --azurite
   dotnet fsi scripts/migrate-posts/migrate-posts.fsx --azurite
   ```

On 2026-10-21, the first run wrote 22 blobs. The second run wrote 0 blobs, and each line said
`the blob is already in the store`. The name, the ETag, the metadata, and the content type of each
blob did not change. This list shows the blobs after the second run:

```text
published/contributing-to-the-fsharp-open-source-ecosystem.md                  migrated=true  text/markdown; charset=utf-8
published/danger-lurks-in-system-linq-enumerable-or-default-functions.md       migrated=true  text/markdown; charset=utf-8
published/fable-reminders-part-1.md                                            migrated=true  text/markdown; charset=utf-8
published/fable-reminders-part-2.md                                            migrated=true  text/markdown; charset=utf-8
published/functional-architecture-in-csharp-a-case-study.md                    migrated=true  text/markdown; charset=utf-8
published/functional-programming-more-than-just-a-coding-style.md              migrated=true  text/markdown; charset=utf-8
published/nighthawks-roadmap-for-zcash-with-aditya-bharadwaj-and-matthew-watt-zcon4.md  migrated=true  text/markdown; charset=utf-8
published/securely-save-a-swiftui-view-as-a-password-protected-pdf.md          migrated=true  text/markdown; charset=utf-8
published/smatterings-of-fsharp.md                                             migrated=true  text/markdown; charset=utf-8
published/why-the-fsharp-would-i-write-real-code-like-this.md                  migrated=true  text/markdown; charset=utf-8
revisions/contributing-to-the-fsharp-open-source-ecosystem/2025-04-11T13:00:00.0000000Z.md          text/markdown; charset=utf-8
revisions/danger-lurks-in-system-linq-enumerable-or-default-functions/2026-02-04T13:00:00.0000000Z.md  text/markdown; charset=utf-8
revisions/fable-reminders-part-1/2024-11-01T13:00:00.0000000Z.md                                     text/markdown; charset=utf-8
revisions/fable-reminders-part-2/2024-11-15T14:00:00.0000000Z.md                                     text/markdown; charset=utf-8
revisions/functional-architecture-in-csharp-a-case-study/2025-11-03T14:00:00.0000000Z.md             text/markdown; charset=utf-8
revisions/functional-programming-more-than-just-a-coding-style/2025-07-24T13:00:00.0000000Z.md       text/markdown; charset=utf-8
revisions/nighthawks-roadmap-for-zcash-with-aditya-bharadwaj-and-matthew-watt-zcon4/2023-08-01T13:00:00.0000000Z.md  text/markdown; charset=utf-8
revisions/securely-save-a-swiftui-view-as-a-password-protected-pdf/2023-12-20T14:00:00.0000000Z.md   text/markdown; charset=utf-8
revisions/smatterings-of-fsharp/2025-11-14T01:30:00.0000000Z.md                                      text/markdown; charset=utf-8
revisions/why-the-fsharp-would-i-write-real-code-like-this/2024-12-19T14:00:00.0000000Z.md           text/markdown; charset=utf-8
series/fable-reminders.yml                                                                           application/yaml; charset=utf-8
series/matts-footgun-collection.yml                                                                  application/yaml; charset=utf-8
```

Each blob had the same bytes as its file in Git. A download and `cmp` of each blob showed this.
