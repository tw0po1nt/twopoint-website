namespace TwoPoint.Http.Endpoints.Admin

/// `Kind` is `field` or `body`. `Field` is null for a body place, and `Line` is 0 for a field place.
[<AllowNullLiteral>]
type ProblemPlaceDto(kind : string, field : string, line : int) =
  member _.Kind = kind
  member _.Field = field
  member _.Line = line

/// `Place` is null for a problem about the whole post.
type ProblemDto =
  { Severity : string
    Place : ProblemPlaceDto
    Message : string }

module ProblemMapping =

  open TwoPoint.Site.Render

  let problem (problem : Problem) : ProblemDto =
    { Severity =
        match problem.Severity with
        | Severity.Error -> "error"
        | Severity.Warning -> "warning"
      Place =
        match problem.Place with
        | Place.Field(name, _) -> ProblemPlaceDto("field", name, 0)
        | Place.Body line -> ProblemPlaceDto("body", null, PostText.editorLine line)
        | Place.Post _ -> null
      Message = problem.Message }
