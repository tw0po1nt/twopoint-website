[<RequireQualifiedAccess>]
module TwoPoint.Admin.Preview

open System

type State =
  | Closed
  /// The API has not answered yet.
  | Loading
  | Shown of Api.PostPreview
  | Failed of reason: string

let isOpen (state: State) = state <> Closed

/// The deploy writes the site CSS and its fonts to this path.
let stylesheet = "/preview-styles/site.css"

let private outlineStart = "<aside class=\"outline\""

/// The article is a `Fragment` of the library check. The outline is its last element, and the site puts it next to the article.
let frame (article: string) : string =
  let split = article.LastIndexOf(outlineStart, StringComparison.Ordinal)

  let content, outline =
    if split < 0 then article, "" else article.Substring(0, split), article.Substring split

  $"<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><link rel=\"stylesheet\" href=\"{stylesheet}\"></head><body><main class=\"post-page\"><article class=\"post\">{content}</article>{outline}</main></body></html>"

/// For example `Body line 3: The output has no page at /posts/missing/.`
let problemText (problem: Api.PreviewProblem) =
  match problem.Place with
  | "Post" -> problem.Message
  | place -> $"{place}: {problem.Message}"

let severityText (problem: Api.PreviewProblem) =
  if problem.Severity = "error" then "Error" else "Warning"
