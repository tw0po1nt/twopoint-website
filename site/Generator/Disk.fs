[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.Disk

open System.IO

let siteFolder = "website"
let newsletterFolder = "newsletter"
let stageMarkerFile = "stage"

let private siteDir (outputFolder: string) = Path.Combine(outputFolder, siteFolder)
let private newsletterDir (outputFolder: string) = Path.Combine(outputFolder, newsletterFolder)

let filePath (outputFolder: string) output =
  match output with
  | SiteFile path -> Path.Combine(siteDir outputFolder, path)
  | Newsletter slug -> Path.Combine(newsletterDir outputFolder, $"{slug}.html")
  | StageMarker -> Path.Combine(outputFolder, stageMarkerFile)

/// Deletes all of the output folder before it writes.
let write (outputFolder: string) (map: RenderMap) =
  if Directory.Exists outputFolder then
    Directory.Delete(outputFolder, true)

  Directory.CreateDirectory(siteDir outputFolder) |> ignore
  Directory.CreateDirectory(newsletterDir outputFolder) |> ignore

  for KeyValue(output, render) in map do
    let path = filePath outputFolder output
    Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
    File.WriteAllBytes(path, render ())

let readStageMarker (outputFolder: string) =
  let path = filePath outputFolder StageMarker
  if File.Exists path then Some(File.ReadAllText path) else None

type NewsletterFile = { Slug: string; Html: byte array }

/// Gives the newsletters in slug order.
let readNewsletters (outputFolder: string) =
  let folder = newsletterDir outputFolder

  if Directory.Exists folder then
    Directory.GetFiles(folder, "*.html")
    |> Array.sort
    |> Array.map (fun path ->
      { Slug = Path.GetFileNameWithoutExtension path
        Html = File.ReadAllBytes path })
    |> List.ofArray
  else
    []
