module TwoPoint.Site.Generator.Tests.StageTests

open TwoPoint.Site.Generator

open Expecto
open System

[<Tests>]
let tests =
  testList "Stage" [
    test "the local record gives each local value" {
      Expect.equal
        (Stage.config Stage.Local)
        { SiteUrl = Uri "http://localhost:4321/"
          SiteTitle = "TwoPoint Local"
          Description = "(Local) A blog about programming, mostly functional"
          OgSiteName = "TwoPoint (Local)"
          Indexable = false
          PulseSiteId = None
          ApiUrl = Uri "http://localhost:7071" }
        "The local record has the local values"
    }

    test "the staging record gives each staging value" {
      Expect.equal
        (Stage.config Stage.Staging)
        { SiteUrl = Uri "https://staging.twopoint.dev/"
          SiteTitle = "TwoPoint Staging"
          Description = "(Staging) A blog about programming, mostly functional"
          OgSiteName = "TwoPoint (Staging)"
          Indexable = false
          PulseSiteId = None
          ApiUrl = Uri "https://func-twopoint-website-api.azurewebsites.net" }
        "The staging record has the staging values"
    }

    test "the production record gives each production value" {
      Expect.equal
        (Stage.config Stage.Production)
        { SiteUrl = Uri "https://twopoint.dev/"
          SiteTitle = "TwoPoint"
          Description = "A blog about programming, mostly functional"
          OgSiteName = "TwoPoint"
          Indexable = true
          PulseSiteId = Some "ps_wKUsetBa02nUCCLQY4Lt"
          ApiUrl = Uri "https://func-twopoint-website-api.azurewebsites.net" }
        "The production record has the production values"
    }

    test "parse reads the name of each stage" {
      for stage in Stage.all do
        Expect.equal (Stage.parse (Stage.name stage)) (Some stage) $"parse reads '{Stage.name stage}'"
    }

    test "parse gives nothing for an unknown name" {
      Expect.isNone (Stage.parse "Production") "Stage names are lower case"
    }
  ]
