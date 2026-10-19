[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.Site

open FsToolkit.ErrorHandling
open System
open System.IO

let private postsFolder = Path.Combine("site", "content", "posts")
let private assetsFolder = Path.Combine("site", "content", "assets")
let private stylesFolder = Path.Combine("site", "content", "styles")
let private privacyFile = "site/content/privacy.md"

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

let loadPosts (fetch: IImageFetch) (root: string) : Result<Post list, PostError list> =
  Directory.GetFiles(Path.Combine(root, postsFolder), "*.md")
  |> Array.sort
  |> List.ofArray
  |> List.traverseResultA (fun path ->
    let file = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/')
    Post.parse fetch file (File.ReadAllText path))
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

let loadPrivacy (fetch: IImageFetch) (root: string) : Result<MarkdownPage, PostError list> =
  MarkdownPage.parse fetch privacyFile (File.ReadAllText(Path.Combine(root, privacyFile)))

let loadContent (fetch: IImageFetch) (root: string) : Result<Content, string list> =
  result {
    let! posts, privacy, assets =
      validation {
        let! posts = loadPosts fetch root |> Result.mapError (List.map PostError.format)
        and! privacy = loadPrivacy fetch root |> Result.mapError (List.map PostError.format)
        and! assets = loadAssets root
        return posts, privacy, assets
      }

    let! ogImages = OgImages.create fetch assets.DefaultImage posts

    return
      { Posts = posts
        Privacy = privacy
        Assets = assets
        OgImages = ogImages }
  }

let loadWidgets (root: string) : Result<Widgets, string list> =
  let folder = Path.Combine(root, WidgetManifest.folder)

  let files =
    if Directory.Exists folder then
      Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
      |> Array.map (fun path -> Path.GetRelativePath(folder, path).Replace(Path.DirectorySeparatorChar, '/'), File.ReadAllBytes path)
      |> Map.ofArray
    else
      Map.empty

  Widgets.create files

/// Gives an error for each internal link of the posts and the privacy page that has no target in the map.
let renderMap (stage: Stage) (buildTime: DateTimeOffset) (widgets: Widgets) (content: Content) : Result<RenderMap, string list> =
  let map = RenderMap.create stage buildTime widgets content
  let sources = Links.ofPage PrivacyPage.urlPath content.Privacy :: List.map Links.ofPost content.Posts

  match Links.check (RenderMap.sitePaths map) sources with
  | [] -> Ok map
  | errors -> Error(List.map PostError.format errors)

let load (fetch: IImageFetch) (root: string) (stage: Stage) (buildTime: DateTimeOffset) : Result<RenderMap, string list> =
  result {
    let! widgets = loadWidgets root
    let! content = loadContent fetch root
    return! renderMap stage buildTime widgets content
  }
