module TwoPoint.Site.Generator.Program

open FsToolkit.ErrorHandling
open System
open System.IO

let private outputFolder (root: string) = Path.Combine(root, "frontend", "dist")

let private build (root: string) (stage: Stage) =
  result {
    let! map = Site.load (Images.ofFetch (ImageFetch.create ())) root stage DateTimeOffset.UtcNow
    let folder = outputFolder root
    Disk.write folder map
    return $"Wrote {map.Count} files for the {Stage.name stage} stage to {folder}"
  }

let private upload (root: string) (container: Uri) =
  let folder = outputFolder root

  Upload.run (BlobContainer.azure container) (Disk.readStageMarker folder) (Disk.readNewsletters folder)
  |> Result.map (fun names ->
    [ for name in names do
        $"Uploaded {name}"
      $"Uploaded {names.Length} newsletters to {container}" ]
    |> String.concat Environment.NewLine)
  |> Result.mapError List.singleton

[<EntryPoint>]
let main argv =
  let result =
    result {
      let! command = Cli.parse (List.ofArray argv) |> Result.mapError List.singleton
      let! root = Site.root (Directory.GetCurrentDirectory()) |> Result.mapError List.singleton

      match command with
      | Cli.Build stage -> return! build root stage
      | Cli.Upload container -> return! upload root container
    }

  match result with
  | Ok message ->
    printfn "%s" message
    0
  | Error messages ->
    for message in messages do
      eprintfn "%s" message

    2
