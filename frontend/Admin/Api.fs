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

let inline private send<'T>
  (method: HttpMethod)
  (api: string)
  (token: string)
  (path: string)
  : JS.Promise<Result<'T, Failure>> =
  fetchUnsafe $"{api}/api/{path}" [
    Method method
    requestHeaders [ Accept "application/json"; Authorization $"Bearer {token}" ]
  ]
  |> Promise.bind (fun response ->
    if response.Status = 401 || response.Status = 403 then
      Promise.lift (Error Refused)
    else
      response.text ()
      |> Promise.map (fun text ->
        match Decode.Auto.fromString<ApiResponse<'T>> (text, caseStrategy = CamelCase) with
        | Ok { Success = true; Data = Some data } -> Ok data
        | Ok { Success = false; Message = Some reason } -> Error(Failed reason)
        | _ -> Error Invalid))
  |> Promise.catch (fun _ -> Error Unanswered)

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
