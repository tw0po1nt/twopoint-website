/// The posts that are not Markdown yet. A link to one of them passes the link check, and its anchor
/// gets no check.
[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.PendingPosts

open System.IO
open System.Reflection

// TODO(https://github.com/tw0po1nt/twopoint-website-staging/issues/131): delete this list when each post is Markdown
let slugs =
  use stream = Assembly.GetExecutingAssembly().GetManifestResourceStream "pending-posts.txt"
  use reader = new StreamReader(stream)

  reader.ReadToEnd().Split('\n')
  |> Array.map _.Trim()
  |> Array.filter (fun line -> line <> "")
  |> Set.ofArray
