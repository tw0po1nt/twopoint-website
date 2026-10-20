module TwoPoint.Site.Generator.Tests.AssetsTests

open TwoPoint.Site.Generator
open TwoPoint.Site.Generator.Tests.Fixture

open Expecto
open SkiaSharp
open System.Text
open System.Text.RegularExpressions

let private style name text : StyleFile = { Name = name; Text = text }

let private listed = Styles.order |> List.map (fun name -> style name $"/* {name} */")

let private asset name (text: string) : Asset =
  { Name = name
    Bytes = Encoding.UTF8.GetBytes text }

let private defaultImage: Asset =
  { Name = "images/default.jpg"
    Bytes = jpeg 1200 630 SKColors.Black }

let private aboutPhoto: Asset =
  { Name = "images/about.jpg"
    Bytes = jpeg 800 1020 SKColors.Gray }

let private favicons =
  [ asset "favicons/favicon.ico" "ico"
    asset "favicons/favicon.svg" "svg"
    asset "favicons/apple-touch-icon.png" "png" ]

let private required = favicons @ [ defaultImage; aboutPhoto ]

let private create assets css =
  match Assets.create assets css with
  | Ok assets -> assets
  | Error errors -> failtest (String.concat "\n" errors)

let private stylesheet (assets: Assets) =
  Encoding.UTF8.GetString(assets.Files[assets.Stylesheet.TrimStart '/'])

[<Tests>]
let tests =
  testList "Assets" [
    test "the site CSS joins the listed files in the listed order" {
      Expect.equal
        (Styles.join (List.rev listed))
        (Ok(Styles.order |> List.map (fun name -> $"/* {name} */") |> String.concat "\n"))
        "The files join in the listed order"
    }

    test "a CSS file that is not on the list fails the build" {
      Expect.equal
        (Styles.join (listed @ [ style "extra.css" ".extra {}" ]))
        (Error [ "frontend/content/styles/extra.css is not on the style list. Add it to Styles.order, or delete it." ])
        "An unlisted file fails"
    }

    test "a listed CSS file that does not exist fails the build" {
      Expect.equal
        (Styles.join (List.tail listed))
        (Error [ $"frontend/content/styles/{List.head Styles.order} is on the style list, and the file does not exist." ])
        "A missing file fails"
    }

    test "each asset goes under /_/ with a hash of its content" {
      let font = asset "fonts/mono.woff2" "font"
      let assets = create (font :: required) ""
      let path = Assets.sitePath font.Name font.Bytes
      Expect.isMatch path "^_/mono-[0-9a-f]{8}\\.woff2$" "The name has the hash"
      Expect.equal assets.Files[path] font.Bytes "The file has the asset bytes"
      Expect.notEqual (Assets.sitePath font.Name (Encoding.UTF8.GetBytes "new font")) path "New bytes give a new name"
      Expect.isMatch assets.Stylesheet "^/_/site-[0-9a-f]{8}\\.css$" "The site CSS has a hash"
      let svg = Assets.sitePath "favicon.svg" (Encoding.UTF8.GetBytes "svg")
      Expect.equal assets.Favicons.Svg $"/{svg}" "The favicon has a hash"
    }

    test "a CSS reference to an asset gives the hashed URL" {
      let font = asset "fonts/mono.woff2" "font"
      let assets = create (font :: required) "@font-face { src: url(\"../assets/fonts/mono.woff2\") format('woff2'); }"

      Expect.equal
        (stylesheet assets)
        $"@font-face {{ src: url(\"/{Assets.sitePath font.Name font.Bytes}\") format('woff2'); }}"
        "The URL is the hashed path"
    }

    test "a CSS reference to a missing asset fails the build" {
      Expect.equal
        (Assets.create required "a { background: url('../assets/missing.png'); }")
        (Error [ "The site CSS refers to ../assets/missing.png, and frontend/content/assets/ has no such file." ])
        "A missing asset fails"
    }

    test "a missing favicon fails the build" {
      Expect.equal
        (Assets.create (List.tail required) "")
        (Error [ "frontend/content/assets/favicons/ has no favicon.ico." ])
        "Each favicon is necessary"
    }

    test "the default image goes under /_/ with a hash, and its size comes from the file" {
      let assets = create required ""

      Expect.equal
        assets.DefaultImage
        { UrlPath = $"/{Assets.sitePath defaultImage.Name defaultImage.Bytes}"
          Size = { Width = 1200; Height = 630 } }
        "The default image has its hashed path and its size"
    }

    test "a missing default image fails the build" {
      Expect.equal
        (Assets.create (favicons @ [ aboutPhoto ]) "")
        (Error [ "frontend/content/assets/ has no images/default.jpg." ])
        "The default image is necessary"

      Expect.equal
        (Assets.create (favicons @ [ asset "images/default.jpg" "text"; aboutPhoto ]) "")
        (Error [ "frontend/content/assets/images/default.jpg cannot be read: the file is not an image." ])
        "The default image is an image"
    }

    test "the About photo goes under /_/ with a hash, and its size comes from the file" {
      let assets = create required ""

      Expect.equal
        assets.AboutPhoto
        { UrlPath = $"/{Assets.sitePath aboutPhoto.Name aboutPhoto.Bytes}"
          Size = { Width = 800; Height = 1020 } }
        "The About photo has its hashed path and its size"
    }

    test "a missing About photo fails the build" {
      Expect.equal
        (Assets.create (favicons @ [ defaultImage ]) "")
        (Error [ "frontend/content/assets/ has no images/about.jpg." ])
        "The About photo is necessary"
    }

    test "the real default image is 1200×630" {
      Expect.equal realContent.Value.Assets.DefaultImage.Size Images.ogSize "The default image has the Open Graph size"
    }

    test "each URL in the real site CSS is a file in the output" {
      let assets = realContent.Value.Assets
      let urls = [ for found in Regex.Matches(stylesheet assets, """url\("([^"]+)"\)""") -> found.Groups[1].Value ]
      Expect.isNonEmpty urls "The CSS refers to the fonts"

      for url in urls do
        Expect.isTrue (url.StartsWith "/_/" && assets.Files.ContainsKey(url.Substring 1)) $"{url} is in the output"
    }
  ]
