[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.Site

open FsToolkit.ErrorHandling
open System
open System.IO

let private assetsFolder = Path.Combine("frontend", "content", "assets")
let private stylesFolder = Path.Combine("frontend", "content", "styles")
let private privacyFile = "frontend/content/privacy.md"
let private emailFolder = Path.Combine("frontend", "content", "email")

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

let parseSeries (files: PostFiles) : Result<Series list, PostError list> =
  files.Series
  |> List.traverseResultA (fun file -> FrontMatter.parseSeries file.Path (PostFolder.text file))
  |> Result.mapError List.concat

let parsePosts (images: IImages) (files: PostFiles) : Result<Post list, PostError list> =
  parseSeries files
  |> Result.bind (fun series ->
    files.Published
    |> List.traverseResultA (fun file -> Post.parse images series file.Path (PostFolder.text file))
    |> Result.mapError List.concat)

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

let loadPrivacy (images: IImages) (root: string) : Result<MarkdownPage, PostError list> =
  MarkdownPage.parse images privacyFile (File.ReadAllText(Path.Combine(root, privacyFile)))

let loadEmail (root: string) : EmailStyles =
  let read name = File.ReadAllText(Path.Combine(root, emailFolder, name))
  { Inline = read "inline.css"; Embed = read "embed.css" }

let loadContent (images: IImages) (root: string) (postFiles: PostFiles) : Result<Content, string list> =
  result {
    let! posts, privacy, assets =
      validation {
        let! posts = parsePosts images postFiles |> Result.mapError (List.map PostError.format)
        and! privacy = loadPrivacy images root |> Result.mapError (List.map PostError.format)
        and! assets = loadAssets root
        return posts, privacy, assets
      }

    let! ogImages = OgImages.create images assets.DefaultImage posts

    return
      { Posts = posts
        Privacy = privacy
        Assets = assets
        OgImages = ogImages
        Email = loadEmail root }
  }

/// Reads the posts from the folder that `pull` writes.
let loadPulledContent (images: IImages) (root: string) : Result<Content, string list> =
  PostFolder.read root PostFolder.pulled
  |> Result.mapError List.singleton
  |> Result.bind (loadContent images root)

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

let load (images: IImages) (root: string) (stage: Stage) (buildTime: DateTimeOffset) : Result<RenderMap, string list> =
  result {
    let! widgets = loadWidgets root
    let! content = loadPulledContent images root
    return! renderMap stage buildTime widgets content
  }
