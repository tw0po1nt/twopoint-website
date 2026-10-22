namespace TwoPoint.Site.Generator

open TwoPoint.Site.Render

open System

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

  let url (config: StageConfig) (urlPath: string) = Uri(config.SiteUrl, urlPath).AbsoluteUri

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
