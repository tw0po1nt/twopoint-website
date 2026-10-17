namespace TwoPoint.Site.Generator

open System

[<RequireQualifiedAccess>]
type Stage =
  | Local
  | Staging
  | Production

type StageConfig =
  { SiteUrl: Uri
    SiteTitle: string
    Description: string
    OgSiteName: string
    Indexable: bool
    PulseSiteId: string option
    ApiUrl: Uri }

[<RequireQualifiedAccess>]
module Stage =

  let all = [ Stage.Local; Stage.Staging; Stage.Production ]

  let name stage =
    match stage with
    | Stage.Local -> "local"
    | Stage.Staging -> "staging"
    | Stage.Production -> "production"

  let parse (text: string) = all |> List.tryFind (fun stage -> name stage = text)

  let private description = "A blog about programming, mostly functional"

  let private apiUrl = Uri "https://func-twopoint-website-api.azurewebsites.net"

  let config stage =
    match stage with
    | Stage.Local ->
      { SiteUrl = Uri "http://localhost:4321/"
        SiteTitle = "TwoPoint Local"
        Description = $"(Local) {description}"
        OgSiteName = "TwoPoint (Local)"
        Indexable = false
        PulseSiteId = None
        ApiUrl = Uri "http://localhost:7071" }
    | Stage.Staging ->
      { SiteUrl = Uri "https://staging.twopoint.dev/"
        SiteTitle = "TwoPoint Staging"
        Description = $"(Staging) {description}"
        OgSiteName = "TwoPoint (Staging)"
        Indexable = false
        PulseSiteId = None
        ApiUrl = apiUrl }
    | Stage.Production ->
      { SiteUrl = Uri "https://twopoint.dev/"
        SiteTitle = "TwoPoint"
        Description = description
        OgSiteName = "TwoPoint"
        Indexable = true
        PulseSiteId = Some "ps_wKUsetBa02nUCCLQY4Lt"
        ApiUrl = apiUrl }
