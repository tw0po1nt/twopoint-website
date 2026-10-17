[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.Disk

open System.IO

/// The folder that GitHub Pages serves.
let siteFolder = "website"

/// The folder of the newsletters, which `upload` sends to Blob.
let newsletterFolder = "newsletter"

let stageMarkerFile = "stage"

/// The file of an output under the output folder.
let filePath (outputFolder: string) output =
  match output with
  | SiteFile path -> Path.Combine(outputFolder, siteFolder, path)
  | Newsletter slug -> Path.Combine(outputFolder, newsletterFolder, $"{slug}.html")
  | StageMarker -> Path.Combine(outputFolder, stageMarkerFile)

/// Empties the output folder, then runs each render and writes its bytes.
let write (outputFolder: string) (map: RenderMap) =
  if Directory.Exists outputFolder then
    Directory.Delete(outputFolder, true)

  Directory.CreateDirectory(Path.Combine(outputFolder, siteFolder)) |> ignore
  Directory.CreateDirectory(Path.Combine(outputFolder, newsletterFolder)) |> ignore

  for KeyValue(output, render) in map do
    let path = filePath outputFolder output
    Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
    File.WriteAllBytes(path, render ())
