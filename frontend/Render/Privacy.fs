[<RequireQualifiedAccess>]
module TwoPoint.Site.Render.Privacy

open System.IO
open System.Reflection

/// The path from the repo root. The library embeds the file, so a change to it needs a new build of the library.
let file = "frontend/content/privacy.md"

let urlPath = "/privacy/"

let private text () =
  use stream = Assembly.GetExecutingAssembly().GetManifestResourceStream "privacy.md"
  use reader = new StreamReader(stream)
  reader.ReadToEnd()

let parse (images: IImageSize) (stage: Stage) : Result<MarkdownPage * FileProblems, FileProblems> =
  MarkdownPage.parse images stage file (text ())
