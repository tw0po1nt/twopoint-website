module TwoPoint.Site.Generator.Program

open System.IO

/// The `site/dist` folder of the repo that holds the current folder.
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
    Cli.parse (List.ofArray argv)
    |> Result.bind (fun (Cli.Build stage) ->
      outputFolder ()
      |> Result.map (fun folder ->
        let map = RenderMap.create stage
        Disk.write folder map
        $"Wrote {map.Count} files for the {Stage.name stage} stage to {folder}"))

  match result with
  | Ok message ->
    printfn "%s" message
    0
  | Error message ->
    eprintfn "%s" message
    2
