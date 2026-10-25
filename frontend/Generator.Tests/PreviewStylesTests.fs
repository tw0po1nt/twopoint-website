module TwoPoint.Site.Generator.Tests.PreviewStylesTests

open TwoPoint.Site.Generator
open TwoPoint.Site.Generator.Tests.Fixture

open Expecto
open System
open System.IO
open System.Text
open System.Text.RegularExpressions

let private tempFolder () =
  let folder = Path.Combine(Path.GetTempPath(), $"preview-styles-{Guid.NewGuid():N}")
  Directory.CreateDirectory folder |> ignore
  folder

let private urlsIn (css: string) =
  [ for found in Regex.Matches(css, """url\("([^"]+)"\)""") -> found.Groups[1].Value ]

[<Tests>]
let tests =
  testList "PreviewStyles" [
    test "the command reads the output folder" {
      Expect.equal
        (Cli.parse [ "preview-styles"; "--out"; "frontend/Admin/dist/preview-styles" ])
        (Ok(Cli.PreviewStyles "frontend/Admin/dist/preview-styles"))
        "The command has the folder"
    }

    test "the command with no output folder fails with a clear error" {
      for args in [ [ "preview-styles" ]; [ "preview-styles"; "--out" ] ] do
        Expect.equal
          (Cli.parse args)
          (Error "preview-styles needs a folder. Give --out <folder>.")
          "The error names the option"
    }

    test "the site CSS and each font that it refers to go to the output folder" {
      let folder = tempFolder ()

      try
        let count =
          match PreviewStyles.write realRoot.Value folder with
          | Ok count -> count
          | Error errors -> failtest (String.concat "\n" errors)

        let css = File.ReadAllText(Path.Combine(folder, "site.css"))
        let urls = urlsIn css

        Expect.isNonEmpty urls "The site CSS refers to fonts"
        Expect.isTrue (urls |> List.exists (fun url -> url.EndsWith ".woff2")) "The site CSS refers to a woff2 font"

        for url in urls do
          Expect.isMatch url "^\\./[^/]+$" "The URL is relative to the CSS file"
          Expect.isTrue (File.Exists(Path.Combine(folder, url.Substring 2))) $"The folder has {url}"

        Expect.equal count (Directory.GetFiles folder).Length "The count is the number of files"
      finally
        Directory.Delete(folder, true)
    }

    test "the output folder has the bytes of each font" {
      let folder = tempFolder ()

      try
        PreviewStyles.write realRoot.Value folder |> ignore

        let font =
          Directory.GetFiles(folder, "ibm-plex-sans-latin-400-normal-*.woff2") |> Array.exactlyOne

        let source =
          File.ReadAllBytes(
            Path.Combine(realRoot.Value, "frontend", "content", "assets", "fonts", "ibm-plex-sans-latin-400-normal.woff2")
          )

        Expect.equal (File.ReadAllBytes font) source "The font is the file of the site"
      finally
        Directory.Delete(folder, true)
    }

    test "the output folder keeps the files that it has" {
      let folder = tempFolder ()

      try
        File.WriteAllText(Path.Combine(folder, "other.txt"), "other")
        PreviewStyles.write realRoot.Value folder |> ignore
        Expect.isTrue (File.Exists(Path.Combine(folder, "other.txt"))) "The other file stays"
      finally
        Directory.Delete(folder, true)
    }

    test "a site CSS without a font reference gives the CSS file only" {
      let assets: Assets =
        let stylesheet = Encoding.UTF8.GetBytes "a { color: red; }"

        { Files = Map [ "_/site-1a2b3c4d.css", stylesheet ]
          Stylesheet = "/_/site-1a2b3c4d.css"
          Favicons = { Ico = ""; Svg = ""; AppleTouch = "" }
          DefaultImage = Unchecked.defaultof<_>
          AboutPhoto = Unchecked.defaultof<_> }

      Expect.equal (PreviewStyles.files assets |> Map.keys |> List.ofSeq) [ "site.css" ] "The CSS file only"
    }
  ]
