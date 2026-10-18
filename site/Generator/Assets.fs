namespace TwoPoint.Site.Generator

open FsToolkit.ErrorHandling
open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.RegularExpressions

/// `Name` is the path in `site/content/assets/`, with `/`, for example `fonts/x.woff2`.
type Asset = { Name: string; Bytes: byte array }

/// `Name` is the file name in `site/content/styles/`.
type StyleFile = { Name: string; Text: string }

/// Each value is a URL path from the root, for example `/_/favicon-1a2b3c4d.svg`.
type Favicons =
  { Ico: string
    Svg: string
    AppleTouch: string }

type Assets =
  { /// Each file at its site path, for example `_/favicon-1a2b3c4d.svg`.
    Files: Map<string, byte array>
    Stylesheet: string
    Favicons: Favicons
    DefaultImage: OgImage }

[<RequireQualifiedAccess>]
module Styles =

  /// The site CSS is these files, in this order.
  let order = [ "fonts.css"; "theme.css"; "base.css"; "layout.css"; "post.css" ]

  let join (files: StyleFile list) : Result<string, string list> =
    let names = files |> List.map _.Name |> Set.ofList

    let unlisted =
      files
      |> List.filter (fun file -> not (List.contains file.Name order))
      |> List.map (fun file -> $"site/content/styles/{file.Name} is not on the style list. Add it to Styles.order, or delete it.")

    let missing =
      order
      |> List.filter (fun name -> not (names.Contains name))
      |> List.map (fun name -> $"site/content/styles/{name} is on the style list, and the file does not exist.")

    match unlisted @ missing with
    | [] ->
      let byName = files |> List.map (fun file -> file.Name, file.Text) |> Map.ofList
      Ok(order |> List.map (fun name -> byName[name]) |> String.concat "\n")
    | errors -> Error errors

[<RequireQualifiedAccess>]
module Assets =

  let private hash (bytes: byte array) =
    Convert.ToHexStringLower(SHA256.HashData bytes).Substring(0, 8)

  /// For example, `fonts/x.woff2` gives `_/x-1a2b3c4d.woff2`.
  let sitePath (name: string) (bytes: byte array) =
    $"_/{Path.GetFileNameWithoutExtension name}-{hash bytes}{Path.GetExtension name}"

  /// A CSS file refers to an asset by its path from `site/content/styles/`.
  let private reference = Regex("""url\(\s*(["']?)\.\./assets/([^"')]+)\1\s*\)""")

  let private resolve (urls: Map<string, string>) (css: string) =
    let unknown =
      [ for found in reference.Matches css do
          let name = found.Groups[2].Value

          if not (urls.ContainsKey name) then
            $"The site CSS refers to ../assets/{name}, and site/content/assets/ has no such file." ]

    match unknown with
    | [] -> Ok(reference.Replace(css, fun found -> $"url(\"{urls[found.Groups[2].Value]}\")"))
    | errors -> Error(List.distinct errors)

  let private favicons (urls: Map<string, string>) =
    let find name =
      match urls.TryFind $"favicons/{name}" with
      | Some url -> Ok url
      | None -> Error [ $"site/content/assets/favicons/ has no {name}." ]

    validation {
      let! ico = find "favicon.ico"
      and! svg = find "favicon.svg"
      and! appleTouch = find "apple-touch-icon.png"

      return
        { Ico = ico
          Svg = svg
          AppleTouch = appleTouch }
    }

  let private defaultImage (assets: Asset list) (urls: Map<string, string>) =
    let name = "images/default.jpg"

    match assets |> List.tryFind (fun asset -> asset.Name = name) with
    | Some asset ->
      Images.size asset.Bytes
      |> Result.map (fun size -> { UrlPath = urls[name]; Size = size })
      |> Result.mapError (fun reason -> [ $"site/content/assets/{name} cannot be read: {reason}." ])
    | None -> Error [ $"site/content/assets/ has no {name}." ]

  /// `css` is the joined site CSS.
  let create (assets: Asset list) (css: string) : Result<Assets, string list> =
    let paths = assets |> List.map (fun asset -> asset.Name, sitePath asset.Name asset.Bytes)
    let urls = paths |> List.map (fun (name, path) -> name, $"/{path}") |> Map.ofList

    validation {
      let! css = resolve urls css
      and! favicons = favicons urls
      and! defaultImage = defaultImage assets urls
      let bytes = Encoding.UTF8.GetBytes css
      let stylesheet = sitePath "site.css" bytes

      return
        { Files =
            Map
              [ for asset, (_, path) in List.zip assets paths do
                  path, asset.Bytes
                stylesheet, bytes ]
          Stylesheet = $"/{stylesheet}"
          Favicons = favicons
          DefaultImage = defaultImage }
    }
