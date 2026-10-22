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

let update (msg: Msg) (_: Model) : Model =
  match msg with
  | Answered(Ok posts) -> Loaded(summary posts)
  | Answered(Error Api.Refused) -> NotLoaded "The API refused the token of this account."
  | Answered(Error Api.Unanswered) -> NotLoaded "The API did not answer."
  | Answered(Error Api.Invalid) -> NotLoaded "The API sent an answer that the admin cannot read."
