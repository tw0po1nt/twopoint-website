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

type Failure =
  /// The API answered 401 or 403.
  | Refused
  | Unanswered
  | Invalid

let inline private get<'T> (api: string) (token: string) (path: string) : JS.Promise<Result<'T, Failure>> =
  fetchUnsafe $"{api}/api/{path}" [
    Method HttpMethod.GET
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
        | _ -> Error Invalid))
  |> Promise.catch (fun _ -> Error Unanswered)

let getPosts (api: string) (token: string) : JS.Promise<Result<Post list, Failure>> =
  get api token "internal/posts"
