module TwoPoint.Site.Dev.Tests.DevSiteTests

open TwoPoint.Site.Dev
open TwoPoint.Site.Generator

open Expecto
open System
open System.Text

let private bytes (text: string) = Encoding.UTF8.GetBytes text

let private text (bytes: byte array) = Encoding.UTF8.GetString bytes

let private widgets =
  { Manifest =
      { Entries = Map.empty
        Head = "head-C.js"
        DevReload = "dev-reload-D.js" }
    HeadScript = ""
    Files = Map.empty
    DevFiles =
      Map
        [ "_/widgets/dev-reload-D.js", bytes "(()=>{})();"
          "_/widgets/dev-reload-D.js.map", bytes "{}" ] }

let private content =
  { Posts = []
    Privacy =
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
    Assets =
      { Files = Map.empty
        Stylesheet = ""
        Favicons = { Ico = ""; Svg = ""; AppleTouch = "" }
        DefaultImage = { UrlPath = ""; Size = { Width = 0; Height = 0 } }
        AboutPhoto = { UrlPath = ""; Size = { Width = 0; Height = 0 } } }
    OgImages = { Files = Map.empty; ByPost = Map.empty }
    Email = { Inline = ""; Embed = "" } }

let private postError = "site/content/posts/a-post.md:12: The front matter has no 'title'."

let private create previous widgets content =
  DevSite.create previous Stage.Local DateTimeOffset.UnixEpoch widgets content

let private good = create DevSite.empty (Ok widgets) (Ok content)

let private failed = create good (Ok widgets) (Error [ postError; "The site has no <b>stylesheet</b>." ])

let private reloadTag = "<script src=\"/_/widgets/dev-reload-D.js\"></script>"

let private wantContent reply =
  match reply with
  | Content(status, path, bytes) -> status, path, text bytes
  | Redirect target -> failtest $"The reply is a redirect to {target}"

[<Tests>]
let tests =
  testList "DevSite" [
    test "a page gets the dev reload script before the end of the body" {
      let status, path, html = wantContent (DevSite.reply good "/privacy/" "")
      Expect.equal status 200 "The page loads"
      Expect.equal path "privacy/index.html" "The page is HTML"
      Expect.stringContains html $"{reloadTag}</body>" "The script is the last item of the body"
    }

    test "a file that is not HTML gets no dev reload script" {
      let _, _, robots = wantContent (DevSite.reply good "/robots.txt" "")
      Expect.equal robots "User-agent: *\nDisallow:\n" "The text does not change"
    }

    test "the not found page gets the dev reload script" {
      let status, _, html = wantContent (DevSite.reply good "/missing/" "")
      Expect.equal status 404 "The status is not found"
      Expect.stringContains html reloadTag "The page reloads after a fix"
    }

    test "the dev server serves the dev reload script and each source map" {
      let status, path, script = wantContent (DevSite.reply good "/_/widgets/dev-reload-D.js" "")
      Expect.equal (status, path, script) (200, "_/widgets/dev-reload-D.js", "(()=>{})();") "The script loads"
      let status, _, map = wantContent (DevSite.reply good "/_/widgets/dev-reload-D.js.map" "")
      Expect.equal (status, map) (200, "{}") "The source map loads"
    }

    test "a model with errors gives a 500 error page for each path" {
      for requestPath in [ "/"; "/privacy/"; "/robots.txt"; "/missing/" ] do
        let status, path, _ = wantContent (DevSite.reply failed requestPath "")
        Expect.equal status 500 $"{requestPath} gives the error status"
        Expect.stringEnds path ".html" $"{requestPath} gives an HTML page"
    }

    test "the error page lists each error with the file, the line, and the message" {
      let _, _, html = wantContent (DevSite.reply failed "/" "")

      Expect.stringContains
        html
        "<li><code>site/content/posts/a-post.md:12</code> The front matter has no &#39;title&#39;.</li>"
        "The post error"

      Expect.stringContains html "<li>The site has no &lt;b&gt;stylesheet&lt;/b&gt;.</li>" "An error with no file, as text"
    }

    test "the error page contains the dev reload script" {
      let _, _, html = wantContent (DevSite.reply failed "/" "")
      Expect.stringContains html $"{reloadTag}</body>" "The page reloads after a fix"
    }

    test "the dev reload script still loads while the model has errors" {
      let status, _, _ = wantContent (DevSite.reply failed "/_/widgets/dev-reload-D.js" "")
      Expect.equal status 200 "The error page can reload"
    }

    test "a failed widget load keeps the dev files of the last good load" {
      let broken = create good (Error [ "site/Widgets/dist/manifest.json does not exist." ]) (Ok content)
      Expect.equal broken.ReloadScript good.ReloadScript "The script stays"
      Expect.equal broken.DevFiles good.DevFiles "The files stay"
      let _, _, html = wantContent (DevSite.reply broken "/" "")
      Expect.stringContains html "site/Widgets/dist/manifest.json does not exist." "The error shows"
      Expect.stringContains html reloadTag "The page reloads after a fix"
    }

    test "a failed widget load and a failed content load give each error" {
      let broken = create good (Error [ "A widget error." ]) (Error [ postError ])
      Expect.equal (Expect.wantError broken.Map "The load fails") [ "A widget error."; postError ] "Both errors show"
    }

    test "the next good load shows the site again" {
      let fixedSite = create failed (Ok widgets) (Ok content)
      let status, _, _ = wantContent (DevSite.reply fixedSite "/privacy/" "")
      Expect.equal status 200 "The page loads"
    }

    test "a folder path with no '/' at the end redirects" {
      Expect.equal (DevSite.reply good "/privacy" "?a=b") (Redirect "/privacy/?a=b") "The redirect keeps the query string"
    }

    test "a page with no body end gets the script at the end" {
      Expect.equal
        (text (DevSite.withReloadScript "/r.js" (bytes "<p>A</p>")))
        "<p>A</p><script src=\"/r.js\"></script>"
        "The script is last"
    }
  ]
