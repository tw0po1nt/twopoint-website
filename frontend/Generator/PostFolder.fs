namespace TwoPoint.Site.Generator

open System.IO
open System.Text

/// `Path` is the path from the repo root. Each build error names it.
type PostFile = { Path: string; Bytes: byte array }

/// The published posts and the series records, each list in path order.
type PostFiles =
  { Published: PostFile list
    Series: PostFile list }

/// A container of the post store that the build reads.
[<RequireQualifiedAccess>]
type Container =
  | Published
  | Series

[<RequireQualifiedAccess>]
module Container =

  let name container =
    match container with
    | Container.Published -> "published"
    | Container.Series -> "series"

  let extension container =
    match container with
    | Container.Published -> ".md"
    | Container.Series -> ".yml"

/// A post folder has one subfolder for each container that the build reads, with the blob names of the post store.
[<RequireQualifiedAccess>]
module PostFolder =

  /// Git ignores this folder.
  let pulled = "frontend/content/posts"

  let filePath (folder: string) (container: Container) (name: string) =
    $"{folder}/{Container.name container}/{name}"

  let text (file: PostFile) =
    use reader = new StreamReader(new MemoryStream(file.Bytes), Encoding.UTF8, true)
    reader.ReadToEnd()

  /// `folder` is the path from `root`.
  let read (root: string) (folder: string) : Result<PostFiles, string> =
    let files container =
      let path = Path.Combine(root, folder, Container.name container)

      if Directory.Exists path then
        Directory.GetFiles(path, $"*{Container.extension container}")
        |> Array.sort
        |> Array.map (fun file ->
          { Path = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/')
            Bytes = File.ReadAllBytes file })
        |> List.ofArray
      else
        []

    match files Container.Published with
    | [] ->
      Error
        $"Found no post in {folder}/{Container.name Container.Published}. Run pull --account <name> to download the posts from the post store."
    | posts -> Ok { Published = posts; Series = files Container.Series }

  let write (root: string) (files: PostFiles) =
    let folder = Path.Combine(root, pulled)

    if Directory.Exists folder then
      Directory.Delete(folder, true)

    for file in files.Published @ files.Series do
      let path = Path.Combine(root, file.Path)
      Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
      File.WriteAllBytes(path, file.Bytes)
