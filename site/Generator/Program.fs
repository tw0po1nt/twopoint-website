module TwoPoint.Site.Generator.Program

open FsToolkit.ErrorHandling
open System.IO

let private outputFolder () =
  let rec find (folder: DirectoryInfo) =
    if isNull folder then
      Error "Cannot find TwoPoint.slnx in the current folder or a parent folder."
    elif File.Exists(Path.Combine(folder.FullName, "TwoPoint.slnx")) then
      Ok(Path.Combine(folder.FullName, "site", "dist"))
    else
      find folder.Parent

  find (DirectoryInfo(Directory.GetCurrentDirectory()))

[<EntryPoint>]
let main argv =
  let result =
    result {
      let! (Cli.Build stage) = Cli.parse (List.ofArray argv)
      let! folder = outputFolder ()
      let map = RenderMap.create stage
      Disk.write folder map
      return $"Wrote {map.Count} files for the {Stage.name stage} stage to {folder}"
    }

  match result with
  | Ok message ->
    printfn "%s" message
    0
  | Error message ->
    eprintfn "%s" message
    2
