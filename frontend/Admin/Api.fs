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

type PostState =
  | Draft
  | Published
  | Upcoming

type Post =
  { Slug: string
    Title: string
    State: PostState
    PendingEdit: bool
    PublishDate: string option
    CommentStats: CommentStats }

type CommenterStatus =
  | New
  | Trusted
  | Banned

type Commenter =
  { Id: string
    Email: string
    Name: string
    Status: CommenterStatus
    CreatedDate: string }

[<RequireQualifiedAccess>]
type CommentApproval =
  | New
  | Approved
  | Rejected

type Comment =
  { Id: string
    CreatedDate: string
    Commenter: Commenter
    Status: CommentApproval
    Content: string }

type PostedComment =
  { PostSlug: string
    PostTitle: string
    Comment: Comment }

type CommenterComments =
  { Commenter: Commenter
    Comments: PostedComment list }

type Approval =
  | Approved
  | Rejected

type Subscription =
  { Id: string
    Email: string
    Name: string option
    CreatedDate: string
    IsActive: bool
    CanceledDate: string option }

[<RequireQualifiedAccess>]
type DeliveryType =
  | Welcome
  | NewPost

type Delivery =
  { Id: string
    CreatedDate: string
    Type: DeliveryType
    PostSlug: string
    PostTitle: string }

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

let private postState: Decoder<PostState> =
  Decode.string
  |> Decode.andThen (fun name ->
    match name with
    | "draft" -> Decode.succeed Draft
    | "published" -> Decode.succeed Published
    | "upcoming" -> Decode.succeed Upcoming
    | _ -> Decode.fail $"The post state '{name}' is not known.")

let private commenterStatusName (status: CommenterStatus) =
  match status with
  | New -> "new"
  | Trusted -> "trusted"
  | Banned -> "banned"

let private commenterStatus: Decoder<CommenterStatus> =
  Decode.string
  |> Decode.andThen (fun name ->
    match name with
    | "new" -> Decode.succeed New
    | "trusted" -> Decode.succeed Trusted
    | "banned" -> Decode.succeed Banned
    | _ -> Decode.fail $"The commenter status '{name}' is not known.")

let private commentApprovalName (approval: CommentApproval) =
  match approval with
  | CommentApproval.New -> "new"
  | CommentApproval.Approved -> "approved"
  | CommentApproval.Rejected -> "rejected"

let private commentApproval: Decoder<CommentApproval> =
  Decode.string
  |> Decode.andThen (fun name ->
    match name with
    | "new" -> Decode.succeed CommentApproval.New
    | "approved" -> Decode.succeed CommentApproval.Approved
    | "rejected" -> Decode.succeed CommentApproval.Rejected
    | _ -> Decode.fail $"The comment approval '{name}' is not known.")

let private deliveryTypeName (deliveryType: DeliveryType) =
  match deliveryType with
  | DeliveryType.Welcome -> "welcome"
  | DeliveryType.NewPost -> "newpost"

let private deliveryType: Decoder<DeliveryType> =
  Decode.string
  |> Decode.andThen (fun name ->
    match name with
    | "welcome" -> Decode.succeed DeliveryType.Welcome
    | "newpost" -> Decode.succeed DeliveryType.NewPost
    | _ -> Decode.fail $"The delivery type '{name}' is not known.")

let private coders =
  Extra.empty
  |> Extra.withCustom (fun (_: PostState) -> failwith "The admin sends no post state.") postState
  |> Extra.withCustom (commenterStatusName >> Encode.string) commenterStatus
  |> Extra.withCustom (commentApprovalName >> Encode.string) commentApproval
  |> Extra.withCustom (deliveryTypeName >> Encode.string) deliveryType

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
        match Decode.Auto.fromString<ApiResponse<'T>> (text, caseStrategy = CamelCase, extra = coders) with
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

let private act (method: HttpMethod) (api: string) (token: string) (path: string) (body: string option) =
  request<unit> method api token path body
  |> Promise.map (
    Result.bind (fun answer ->
      match answer with
      | { Success = true } -> Ok()
      | _ -> Error Invalid)
  )

let private commentsPath (slug: string) =
  $"internal/posts/{JS.encodeURIComponent slug}/comments"

let private commentPath (slug: string) (comment: Comment) =
  $"{commentsPath slug}/{JS.encodeURIComponent comment.Id}"

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

let getModerationQueue (api: string) (token: string) : JS.Promise<Result<PostedComment list, Failure>> =
  send HttpMethod.GET api token "internal/comments"

let getPostComments (api: string) (token: string) (slug: string) : JS.Promise<Result<Comment list, Failure>> =
  send HttpMethod.GET api token (commentsPath slug)

let changeApproval
  (api: string)
  (token: string)
  (slug: string)
  (comment: Comment)
  (approval: Approval)
  : JS.Promise<Result<unit, Failure>> =
  let value =
    match approval with
    | Approved -> "approved"
    | Rejected -> "rejected"

  Encode.object [ "approval", Encode.string value ]
  |> Encode.toString 0
  |> Some
  |> act HttpMethod.PUT api token (commentPath slug comment)

let deleteComment (api: string) (token: string) (slug: string) (comment: Comment) : JS.Promise<Result<unit, Failure>> =
  act HttpMethod.DELETE api token (commentPath slug comment) None

let private commenterPath (commenterId: string) =
  $"internal/commenters/{JS.encodeURIComponent commenterId}"

let getCommenters (api: string) (token: string) : JS.Promise<Result<Commenter list, Failure>> =
  send HttpMethod.GET api token "internal/commenters"

let getCommenter (api: string) (token: string) (commenterId: string) : JS.Promise<Result<CommenterComments, Failure>> =
  send HttpMethod.GET api token (commenterPath commenterId)

let changeCommenterStatus
  (api: string)
  (token: string)
  (commenterId: string)
  (status: CommenterStatus)
  : JS.Promise<Result<unit, Failure>> =
  Encode.object [ "status", Encode.string (commenterStatusName status) ]
  |> Encode.toString 0
  |> Some
  |> act HttpMethod.PUT api token (commenterPath commenterId)

let deleteCommenter (api: string) (token: string) (commenterId: string) : JS.Promise<Result<unit, Failure>> =
  act HttpMethod.DELETE api token (commenterPath commenterId) None

let getSubscriptions (api: string) (token: string) : JS.Promise<Result<Subscription list, Failure>> =
  send HttpMethod.GET api token "internal/subscriptions"

let private subscriptionPath (subscriptionId: string) =
  $"internal/subscriptions/{JS.encodeURIComponent subscriptionId}"

/// The answer is the subscription as the backend stored it, with its canceled date.
let cancelSubscription (api: string) (token: string) (subscriptionId: string) : JS.Promise<Result<Subscription, Failure>> =
  send HttpMethod.POST api token $"{subscriptionPath subscriptionId}/cancellations"

let deleteSubscription (api: string) (token: string) (subscriptionId: string) : JS.Promise<Result<unit, Failure>> =
  act HttpMethod.DELETE api token (subscriptionPath subscriptionId) None

let getSubscriptionDeliveries
  (api: string)
  (token: string)
  (subscriptionId: string)
  : JS.Promise<Result<Delivery list, Failure>> =
  send HttpMethod.GET api token $"internal/subscriptions/{JS.encodeURIComponent subscriptionId}/deliveries"
