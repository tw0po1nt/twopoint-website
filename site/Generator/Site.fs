[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.Site

open FsToolkit.ErrorHandling
open System.IO

let private postsFolder = Path.Combine("site", "content", "posts")
let private assetsFolder = Path.Combine("site", "content", "assets")
let private stylesFolder = Path.Combine("site", "content", "styles")

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

let loadAssets (root: string) : Result<Assets, string list> =
  let assetsDir = Path.Combine(root, assetsFolder)

  let assets =
    Directory.GetFiles(assetsDir, "*", SearchOption.AllDirectories)
    |> Array.filter (fun path -> not (Path.GetFileName(path).StartsWith '.'))
    |> Array.sort
    |> Array.map (fun path ->
      { Name = Path.GetRelativePath(assetsDir, path).Replace(Path.DirectorySeparatorChar, '/')
        Bytes = File.ReadAllBytes path })
    |> List.ofArray

  let styles =
    Directory.GetFiles(Path.Combine(root, stylesFolder), "*.css")
    |> Array.sort
    |> Array.map (fun path -> { StyleFile.Name = Path.GetFileName path; Text = File.ReadAllText path })
    |> List.ofArray

  Styles.join styles |> Result.bind (Assets.create assets)

let loadContent (root: string) : Result<Content, string list> =
  validation {
    let! posts = loadPosts root |> Result.mapError (List.map PostError.format)
    and! assets = loadAssets root
    return { Posts = posts; Assets = assets }
  }

let load (root: string) (stage: Stage) : Result<RenderMap, string list> =
  result {
    let! content = loadContent root
    let map = RenderMap.create stage content

    match Links.check (RenderMap.sitePaths map) content.Posts with
    | [] -> return map
    | errors -> return! Error(List.map PostError.format errors)
  }
