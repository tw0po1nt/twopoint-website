/// The pages that a post links to and that the output does not contain yet.
[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.PendingPages

open System.IO
open System.Reflection

// TODO(https://github.com/tw0po1nt/twopoint-website-staging/issues/140): delete this list when the list pages are in the output
let urlPaths =
  use stream = Assembly.GetExecutingAssembly().GetManifestResourceStream "pending-pages.txt"
  use reader = new StreamReader(stream)

  reader.ReadToEnd().Split('\n')
  |> Array.map _.Trim()
  |> Array.filter (fun line -> line <> "")
  |> Set.ofArray
