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

let load (root: string) (stage: Stage) : Result<RenderMap, string list> =
  result {
    let! posts = loadPosts root
    let map = RenderMap.create stage posts

    match Links.check (RenderMap.sitePaths map) posts with
    | [] -> return map
    | errors -> return! Error errors
  }
  |> Result.mapError (List.map PostError.format)
