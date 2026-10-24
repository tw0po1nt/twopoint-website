[<RequireQualifiedAccess>]
module TwoPoint.Admin.Api

open TwoPoint.Api

open Fable.Core
open Fetch
open Thoth.Json

type CommentStats =
  { New: int
    Approved: int
    Rejected: int }

type Post =
  { Slug: string
    Title: string
    CommentStats: CommentStats }

type Commenter =
  { Email: string
    Name: string
    Status: string }

type Comment =
  { Id: string
    CreatedDate: string
    Commenter: Commenter
    Status: string
    Content: string }

type QueuedComment =
  { PostSlug: string
    PostTitle: string
    Comment: Comment }

type Approval =
  | Approved
  | Rejected

type Rebuild =
  { Dispatch: string
    ProductionRun: int option }

type Rebuilt =
  | Queued of productionRun: int
  | QueuedWithoutRunNumber
  | Logged

type Failure =
  /// The API answered 401 or 403.
  | Refused
  | Unanswered
  | Invalid
  | Failed of reason: string

let reason (failure: Failure) =
  match failure with
  | Refused -> "The API refused the token of this account."
  | Unanswered -> "The API did not answer."
  | Invalid -> "The API sent an answer that the admin cannot read."
  | Failed reason -> reason

let inline private request<'T>
  (method: HttpMethod)
  (api: string)
  (token: string)
  (path: string)
  (body: string option)
  : JS.Promise<Result<ApiResponse<'T>, Failure>> =
  fetchUnsafe $"{api}/api/{path}" [
    Method method
    requestHeaders [
      Accept "application/json"
      Authorization $"Bearer {token}"
      if body.IsSome then
        ContentType "application/json"
    ]
    if body.IsSome then
      Body(U3.Case3 body.Value)
  ]
  |> Promise.bind (fun response ->
    if response.Status = 401 || response.Status = 403 then
      Promise.lift (Error Refused)
    else
      response.text ()
      |> Promise.map (fun text ->
        match Decode.Auto.fromString<ApiResponse<'T>> (text, caseStrategy = CamelCase) with
        | Ok { Success = false; Message = Some reason } -> Error(Failed reason)
        | Ok answer -> Ok answer
        | Error _ -> Error Invalid))
  |> Promise.catch (fun _ -> Error Unanswered)

let inline private send<'T> (method: HttpMethod) (api: string) (token: string) (path: string) : JS.Promise<Result<'T, Failure>> =
  request<'T> method api token path None
  |> Promise.map (
    Result.bind (fun answer ->
      match answer with
      | { Success = true; Data = Some data } -> Ok data
      | _ -> Error Invalid)
  )

/// For a call whose answer has no data.
let private act (method: HttpMethod) (api: string) (token: string) (path: string) (body: string option) =
  request<unit> method api token path body
  |> Promise.map (
    Result.bind (fun answer ->
      match answer with
      | { Success = true } -> Ok()
      | _ -> Error Invalid)
  )

let private commentPath (comment: QueuedComment) =
  $"internal/posts/{JS.encodeURIComponent comment.PostSlug}/comments/{JS.encodeURIComponent comment.Comment.Id}"

let getPosts (api: string) (token: string) : JS.Promise<Result<Post list, Failure>> =
  send HttpMethod.GET api token "internal/posts"

let rebuildSite (api: string) (token: string) : JS.Promise<Result<Rebuilt, Failure>> =
  send<Rebuild> HttpMethod.POST api token "internal/deploys"
  |> Promise.map (
    Result.bind (fun rebuild ->
      match rebuild.Dispatch, rebuild.ProductionRun with
      | "queued", Some run -> Ok(Queued run)
      | "queued", None -> Ok QueuedWithoutRunNumber
      | "logged", None -> Ok Logged
      | _ -> Error Invalid)
  )

let getModerationQueue (api: string) (token: string) : JS.Promise<Result<QueuedComment list, Failure>> =
  send HttpMethod.GET api token "internal/comments"

let changeApproval
  (api: string)
  (token: string)
  (comment: QueuedComment)
  (approval: Approval)
  : JS.Promise<Result<unit, Failure>> =
  let value =
    match approval with
    | Approved -> "approved"
    | Rejected -> "rejected"

  Encode.object [ "approval", Encode.string value ]
  |> Encode.toString 0
  |> Some
  |> act HttpMethod.PUT api token (commentPath comment)

let deleteComment (api: string) (token: string) (comment: QueuedComment) : JS.Promise<Result<unit, Failure>> =
  act HttpMethod.DELETE api token (commentPath comment) None
