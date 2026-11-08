[<RequireQualifiedAccess>]
module TwoPoint.Site.Widgets.StopReplyEmails

open TwoPoint.Api
open TwoPoint.Api.ReplyEmails

type Failure =
  | Refused
  | Unreachable

type Model =
  | MissingToken
  | Reading of token: string
  | Unreadable of Failure
  | Active of token: string * postTitle: string
  | Stopping of token: string * postTitle: string
  | StopFailed of token: string * postTitle: string * Failure
  | Stopped
  | Unknown

type Msg =
  | StateRead of ApiResponse<ReplyEmailsState>
  | StateUnread
  | StopSelected
  | StopAnswered of ApiResponse<ReplyEmailsState>
  | StopUnanswered

type Effect =
  | Read of token: string
  | Stop of token: string

/// The stop link of a reply email sets this query key.
[<Literal>]
let private tokenKey = "token"

let init (query: (string * string) list) : Model * Effect option =
  match query |> List.tryFind (fun (name, _) -> name = tokenKey) with
  | Some(_, token) when token.Trim() <> "" -> Reading(token.Trim()), Some(Read(token.Trim()))
  | _ -> MissingToken, None

let private answered (response: ApiResponse<ReplyEmailsState>) =
  if response.Success then response.Data else None

let update (msg: Msg) (model: Model) : Model * Effect option =
  match model, msg with
  | Reading token, StateRead response ->
    match answered response with
    | Some(ReplyEmailsState.Active postTitle) -> Active(token, postTitle), None
    | Some ReplyEmailsState.Stopped -> Stopped, None
    | Some ReplyEmailsState.Unknown -> Unknown, None
    | None -> Unreadable Refused, None
  | Reading _, StateUnread -> Unreadable Unreachable, None
  | Active(token, postTitle), StopSelected
  | StopFailed(token, postTitle, _), StopSelected -> Stopping(token, postTitle), Some(Stop token)
  | Stopping(token, postTitle), StopAnswered response ->
    match answered response with
    | Some ReplyEmailsState.Stopped -> Stopped, None
    | Some ReplyEmailsState.Unknown -> Unknown, None
    | Some(ReplyEmailsState.Active _)
    | None -> StopFailed(token, postTitle, Refused), None
  | Stopping(token, postTitle), StopUnanswered -> StopFailed(token, postTitle, Unreachable), None
  | _ -> model, None
