module TwoPoint.Site.Generator.Program

open FsToolkit.ErrorHandling
open System.IO

[<EntryPoint>]
let main argv =
  let result =
    result {
      let! (Cli.Build stage) = Cli.parse (List.ofArray argv) |> Result.mapError List.singleton
      let! root = Site.root (Directory.GetCurrentDirectory()) |> Result.mapError List.singleton
      let! map = Site.load root stage
      let folder = Path.Combine(root, "site", "dist")
      Disk.write folder map
      return $"Wrote {map.Count} files for the {Stage.name stage} stage to {folder}"
    }

  match result with
  | Ok message ->
    printfn "%s" message
    0
  | Error messages ->
    for message in messages do
      eprintfn "%s" message

    2
