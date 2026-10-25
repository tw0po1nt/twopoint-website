[<RequireQualifiedAccess>]
module TwoPoint.Admin.Preview

type State =
  | Closed
  | Loading
  | Shown of Api.PostPreview
  | Failed of reason: string

let isOpen (state: State) = state <> Closed

/// The path of the output of `preview-styles`.
let stylesheet = "/preview-styles/site.css"

let frame (article: Api.PreviewArticle) : string =
  $"<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><link rel=\"stylesheet\" href=\"{stylesheet}\"></head><body><main class=\"post-page\"><article class=\"post\">{article.Content}</article>{article.Outline}</main></body></html>"

let problemText (problem: Api.PreviewProblem) =
  match problem.Place with
  | Some place -> $"{place}: {problem.Message}"
  | None -> problem.Message

let severityClass (problem: Api.PreviewProblem) =
  match problem.Severity with
  | Api.Severity.Error -> "bad"
  | Api.Severity.Warning -> "warn"

let severityText (problem: Api.PreviewProblem) =
  match problem.Severity with
  | Api.Severity.Error -> "Error"
  | Api.Severity.Warning -> "Warning"
