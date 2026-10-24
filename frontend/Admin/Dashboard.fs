[<RequireQualifiedAccess>]
module TwoPoint.Admin.Dashboard

type Summary = { Posts: int; NewComments: int }

type Model =
  | Waiting
  | Loaded of Summary
  | NotLoaded of reason: string

type Msg = Answered of Result<Api.Post list, Api.Failure>

let init: Model = Waiting

let summary (posts: Api.Post list) =
  { Posts = List.length posts
    NewComments = posts |> List.sumBy _.CommentStats.New }

let newComments (model: Model) =
  match model with
  | Loaded summary -> Some summary.NewComments
  | Waiting
  | NotLoaded _ -> None

let update (msg: Msg) (_: Model) : Model =
  match msg with
  | Answered(Ok posts) -> Loaded(summary posts)
  | Answered(Error failure) -> NotLoaded(Api.reason failure)
