[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.Site

open FsToolkit.ErrorHandling
open System.IO

let private postsFolder = Path.Combine("site", "content", "posts")

/// Gives the folder of `TwoPoint.slnx`, from `start` or a parent folder of it.
let root (start: string) =
  let rec find (folder: DirectoryInfo) =
    if isNull folder then
      Error "Cannot find TwoPoint.slnx in the current folder or a parent folder."
    elif File.Exists(Path.Combine(folder.FullName, "TwoPoint.slnx")) then
      Ok folder.FullName
    else
      find folder.Parent

  find (DirectoryInfo start)

/// Gives the published posts. A draft is not in the list.
let loadPosts (root: string) : Result<Post list, PostError list> =
  Directory.GetFiles(Path.Combine(root, postsFolder), "*.md")
  |> Array.sort
  |> List.ofArray
  |> List.traverseResultA (fun path ->
    let file = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/')
    Post.parse file (File.ReadAllText path))
  |> Result.mapError List.concat
  |> Result.map (
    List.choose (function
      | Published post -> Some post
      | Draft -> None)
  )

/// Reads the content of the repo at `root`, and gives the render map or each error.
let load (root: string) (stage: Stage) : Result<RenderMap, string list> =
  loadPosts root
  |> Result.bind (RenderMap.create stage)
  |> Result.mapError (List.map PostError.format)
