namespace TwoPoint.Site.Render

open System

[<RequireQualifiedAccess>]
type Stage =
  | Local
  | Staging
  | Production

[<RequireQualifiedAccess>]
module Stage =

  let all = [ Stage.Local; Stage.Staging; Stage.Production ]

  let name stage =
    match stage with
    | Stage.Local -> "local"
    | Stage.Staging -> "staging"
    | Stage.Production -> "production"

  let parse (text: string) = all |> List.tryFind (fun stage -> name stage = text)

[<RequireQualifiedAccess>]
module Azurite =

  /// Azurite serves its blob endpoint on port 10000 of the local machine.
  let isUrl (url: Uri) =
    (url.Scheme = Uri.UriSchemeHttp || url.Scheme = Uri.UriSchemeHttps)
    && url.IsLoopback
    && url.Port = 10000

  let isForbidden (stage: Stage) (url: Uri) = isUrl url && stage <> Stage.Local

  let problem (url: Uri) =
    $"The URL {url.AbsoluteUri} is an Azurite URL. Only the local stage accepts an Azurite URL."

/// The public blob container of the images and the PDFs that the author uploads.
[<RequireQualifiedAccess>]
module AssetContainer =

  let url = Uri "https://twopointwebsite.blob.core.windows.net/assets/"

  /// The local backend writes each asset to Azurite.
  let isUrl (stage: Stage) (asset: Uri) =
    url.IsBaseOf asset
    || stage = Stage.Local
       && Azurite.isUrl asset
       && asset.AbsolutePath.StartsWith "/devstoreaccount1/assets/"
