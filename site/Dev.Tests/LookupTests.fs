module TwoPoint.Site.Dev.Tests.LookupTests

open TwoPoint.Site.Dev
open TwoPoint.Site.Generator

open Expecto
open System
open System.Text

let private noAssets =
  { Files = Map.empty
    Stylesheet = ""
    Favicons = { Ico = ""; Svg = ""; AppleTouch = "" }
    DefaultImage = { UrlPath = ""; Size = { Width = 0; Height = 0 } }
    AboutPhoto = { UrlPath = ""; Size = { Width = 0; Height = 0 } } }

let private noPrivacy =
  { File = "site/content/privacy.md"
    Title = "Privacy policy"
    Description = ""
    Body =
      { Html = ""
        EmailHtml = ""
        Headings = []
        Anchors = Set.empty
        Links = []
        ReadingTime = 0 } }

let private noWidgets =
  { Manifest =
      { Entries = Map.empty
        Head = ""
        DevReload = "" }
    HeadScript = ""
    Files = Map.empty
    DevFiles = Map.empty }

let private post slug =
  { File = $"site/content/posts/{slug}.md"
    Slug = slug
    FrontMatter =
      { PublishDate = DateTimeOffset.UnixEpoch
        UpdateDate = None
        Draft = false
        Title = slug
        Excerpt = "An excerpt"
        Author = "Matthew Watt"
        Category = { Slug = "one-offs"; Title = "One-offs" }
        Tags = []
        Series = None
        Image = None
        ShowOnHomepage = true }
    Body = { noPrivacy.Body with EmailHtml = $"<p>The body of {slug}.</p>" } }

let private mapOf (posts: Post list) =
  RenderMap.create
    Stage.Local
    DateTimeOffset.UnixEpoch
    noWidgets
    { Posts = posts
      Privacy = noPrivacy
      Assets = noAssets
      OgImages = { Files = Map.empty; ByPost = Map.empty }
      Email = { Inline = ""; Embed = "" } }

let private map = mapOf []

let private postsMap = mapOf [ post "a-post"; post "b-post" ]

let private text (render: Render) = Encoding.UTF8.GetString(render ())

[<Tests>]
let tests =
  testList "Lookup" [
    test "robots.txt gives the Local robots text" {
      let path, render = Expect.wantSome (Lookup.find map "/robots.txt") "The map contains robots.txt"
      Expect.equal path "robots.txt" "The lookup gives the site path of the file"
      Expect.equal (text render) "User-agent: *\nDisallow:\n" "The dev server serves the Local robots text"
    }

    test "a path that is not in the site gives nothing" {
      Expect.isNone (Lookup.find map "/missing.txt") "A path with no entry gives nothing"
    }

    test "a folder path with no '/' at the end redirects to the folder path" {
      Expect.isNone (Lookup.find map "/unsubscribe") "The path itself is not in the site"
      Expect.equal (Lookup.trailingSlashRedirect map "/unsubscribe" "") (Some "/unsubscribe/") "The redirect adds the '/'"
    }

    test "the redirect keeps the query string" {
      Expect.equal
        (Lookup.trailingSlashRedirect map "/unsubscribe" "?subscriptionId=sub-123")
        (Some "/unsubscribe/?subscriptionId=sub-123")
        "The email link keeps its subscription ID"
    }

    test "a path with a '/' at the end, or with no folder in the site, gives no redirect" {
      Expect.isNone (Lookup.trailingSlashRedirect map "/unsubscribe/" "") "The path is already a folder path"
      Expect.isNone (Lookup.trailingSlashRedirect map "/missing" "") "The site has no such folder"
      Expect.isNone (Lookup.trailingSlashRedirect map "/robots.txt" "") "A file gives no redirect"
    }

    test "/__dev/ links to the newsletter of each post" {
      let path, render = Expect.wantSome (Lookup.find postsMap "/__dev/") "The dev server serves the index"
      Expect.equal path "__dev/index.html" "The index is an HTML page"
      let html = text render
      Expect.stringContains html "<a href=\"/__dev/newsletter/a-post/\">a-post</a>" "The link to the first newsletter"
      Expect.stringContains html "<a href=\"/__dev/newsletter/b-post/\">b-post</a>" "The link to the second newsletter"
    }

    test "the newsletter path of a post gives its newsletter" {
      let path, render =
        Expect.wantSome (Lookup.find postsMap (Lookup.newsletterPath "a-post")) "The dev server serves the newsletter"

      Expect.equal path "__dev/newsletter/a-post/index.html" "The newsletter is an HTML page"
      Expect.equal (text render) (text postsMap[Newsletter "a-post"]) "The page is the newsletter of the post"
      Expect.stringContains (text render) "The body of a-post." "The newsletter contains the post"
    }

    test "the newsletter path of a slug with no post gives nothing" {
      Expect.isNone (Lookup.find postsMap (Lookup.newsletterPath "missing")) "No newsletter"
      Expect.isNone (Lookup.find postsMap "/__dev/newsletter/a-post/extra/") "A path below a newsletter gives nothing"
    }

    test "a dev path with no '/' at the end redirects to the folder path" {
      Expect.equal (Lookup.trailingSlashRedirect postsMap "/__dev" "") (Some "/__dev/") "The index"

      Expect.equal
        (Lookup.trailingSlashRedirect postsMap "/__dev/newsletter/a-post" "")
        (Some "/__dev/newsletter/a-post/")
        "A newsletter"
    }

    test "the stage marker is not a site path" {
      Expect.isNone (Lookup.find map "/stage") "The dev server serves only the site files"
    }
  ]
