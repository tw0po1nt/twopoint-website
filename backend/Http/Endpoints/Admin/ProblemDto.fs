namespace TwoPoint.Http.Endpoints.Admin

/// `Place` is null for a problem about the whole post.
type PreviewProblemDto =
  { Severity : string
    Place : string
    Message : string }

module ProblemMapping =

  open TwoPoint.Site.Render

  let problem (problem : Problem) : PreviewProblemDto =
    { Severity =
        match problem.Severity with
        | Severity.Error -> "error"
        | Severity.Warning -> "warning"
      Place =
        match problem.Place with
        | Place.Field(name, _) -> $"Field {name}"
        | Place.Body line -> $"Body line {line}"
        | Place.Post _ -> null
      Message = problem.Message }
