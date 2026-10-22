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
