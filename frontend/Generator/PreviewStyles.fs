[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.PreviewStyles

open System.IO
open System.Text
open System.Text.RegularExpressions

let styleFile = "site.css"

let private siteUrl = Regex("""url\("/(_/[^"]+)"\)""")

/// Each file by its name in the output folder. The CSS refers to its files with `./` URLs, so it works at any URL.
let files (assets: Assets) : Map<string, byte array> =
  let css = Encoding.UTF8.GetString assets.Files[assets.Stylesheet.TrimStart '/']
  let referred = [ for found in siteUrl.Matches css -> found.Groups[1].Value ] |> List.distinct

  let relative = siteUrl.Replace(css, fun found -> $"url(\"./{Path.GetFileName found.Groups[1].Value}\")")

  Map
    [ styleFile, Encoding.UTF8.GetBytes relative
      for path in referred do
        Path.GetFileName path, assets.Files[path] ]

/// Gives the number of files that it wrote. It keeps the other files of the folder.
let write (root: string) (folder: string) : Result<int, string list> =
  Site.loadAssets root
  |> Result.map (fun assets ->
    let files = files assets
    Directory.CreateDirectory folder |> ignore

    for KeyValue(name, bytes) in files do
      File.WriteAllBytes(Path.Combine(folder, name), bytes)

    files.Count)
