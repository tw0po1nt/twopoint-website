module TwoPoint.Site.Generator.Program

open TwoPoint.Site.Render

open FsToolkit.ErrorHandling
open System
open System.IO

let private outputFolder (root: string) = Path.Combine(root, "frontend", "dist")

let private build (root: string) (stage: Stage) =
  result {
    let! map, warnings = Site.load (Images.ofFetch (ImageFetch.create ())) root stage DateTimeOffset.UtcNow
    let folder = outputFolder root
    Disk.write folder map
    return String.concat Environment.NewLine (warnings @ [ $"Wrote {map.Count} files for the {Stage.name stage} stage to {folder}" ])
  }

let private pull (root: string) (account: string) =
  Pull.run (PostStore.azure account)
  |> Result.map (fun files ->
    PostFolder.write root files

    $"Pulled {files.Published.Length} posts and {files.Series.Length} series records from {account} to {PostFolder.pulled}")
  |> Result.mapError List.singleton

let private upload (root: string) (container: Uri) =
  let folder = outputFolder root

  Upload.run (BlobContainer.azure container) (Disk.readStageMarker folder) (Disk.readNewsletters folder)
  |> Result.map (fun report ->
    [ for name in report.Uploaded do
        $"Uploaded {name}"
      for name in report.Deleted do
        $"Deleted {name}"
      $"Uploaded {report.Uploaded.Length} newsletters to {container} and deleted {report.Deleted.Length} newsletters" ]
    |> String.concat Environment.NewLine)
  |> Result.mapError List.singleton

let private previewStyles (root: string) (out: string) =
  PreviewStyles.write root (Path.GetFullPath(out, Directory.GetCurrentDirectory()))
  |> Result.map (fun count -> $"Wrote {count} files for the preview to {out}")

[<EntryPoint>]
let main argv =
  let result =
    result {
      let! command = Cli.parse (List.ofArray argv) |> Result.mapError List.singleton
      let! root = Site.root (Directory.GetCurrentDirectory()) |> Result.mapError List.singleton

      match command with
      | Cli.Build stage -> return! build root stage
      | Cli.Pull account -> return! pull root account
      | Cli.Upload container -> return! upload root container
      | Cli.PreviewStyles out -> return! previewStyles root out
    }

  match result with
  | Ok message ->
    printfn "%s" message
    0
  | Error messages ->
    for message in messages do
      eprintfn "%s" message

    2
