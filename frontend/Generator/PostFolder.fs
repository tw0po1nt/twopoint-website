namespace TwoPoint.Site.Generator

open System.IO
open System.Text

/// `Path` is the path from the repo root. Each build error names it.
type PostFile = { Path: string; Bytes: byte array }

/// The published posts and the series records, each list in path order.
type PostFiles =
  { Published: PostFile list
    Series: PostFile list }

/// A post folder has one subfolder for each container that the build reads, with the blob names of the post store.
[<RequireQualifiedAccess>]
module PostFolder =

  /// `pull` writes this folder and `build` reads it. Git ignores it.
  let pulled = "frontend/content/posts"

  let published = "published"
  let series = "series"

  let filePath (folder: string) (container: string) (name: string) = $"{folder}/{container}/{name}"

  let text (file: PostFile) =
    use reader = new StreamReader(new MemoryStream(file.Bytes), Encoding.UTF8, true)
    reader.ReadToEnd()

  /// `folder` is the path from `root`. A folder with no post is an error, because the site needs the posts.
  let read (root: string) (folder: string) : Result<PostFiles, string> =
    let files container extension =
      let path = Path.Combine(root, folder, container)

      if Directory.Exists path then
        Directory.GetFiles(path, $"*{extension}")
        |> Array.sort
        |> Array.map (fun file ->
          { Path = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/')
            Bytes = File.ReadAllBytes file })
        |> List.ofArray
      else
        []

    match files published ".md" with
    | [] ->
      Error
        $"Found no post in {folder}/{published}. Run pull --account <name> to download the posts from the post store."
    | posts -> Ok { Published = posts; Series = files series ".yml" }

  /// Deletes the pulled folder first, so a post that left the store leaves the folder.
  let write (root: string) (files: PostFiles) =
    let folder = Path.Combine(root, pulled)

    if Directory.Exists folder then
      Directory.Delete(folder, true)

    for file in files.Published @ files.Series do
      let path = Path.Combine(root, file.Path)
      Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
      File.WriteAllBytes(path, file.Bytes)
