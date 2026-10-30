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

[<RequireQualifiedAccess>]
type DeployState =
  | Deploying
  | Deployed
  | DeployFailed

type Post =
  { Slug: string
    Title: string
    State: PostState
    PendingEdit: bool
    PublishDate: string option
    Deploy: DeployState
    CommentStats: CommentStats }

type PostDeploy = { Deploy: DeployState }

/// `Category` and `Tags` give the text of the front matter. `Series` gives the slug.
type PostDetails =
  { Author: string option
    Category: string option
    Tags: string list
    Series: string option
    Image: string option
    ShowOnHomepage: bool }

/// A category or a tag on the published posts, with its number of posts.
/// `Value` is the text that the front matter keeps. `Title` is the title that the site shows.
type Term = { Slug: string; Title: string; Value: string; Posts: int }

type Terms = { Categories: Term list; Tags: Term list }

type SeriesListing = { Slug: string; Title: string }

[<RequireQualifiedAccess>]
type SeriesPostState =
  | Published
  | Upcoming
  | Draft
  /// Only the pending edit of a published post puts it in the series.
  | PendingEdit

/// `PublishDate` is an ISO 8601 UTC time, and `None` for a draft and a pending edit.
type SeriesPost =
  { Slug: string
    Title: string
    State: SeriesPostState
    PublishDate: string option }

/// `ETag` is the ETag of the record. `Image` is the https URL of the image.
type SeriesRecord =
  { Slug: string
    Title: string
    Excerpt: string
    Image: string option
    ETag: string
    Posts: SeriesPost list }

type ChangedSlug = { Slug: string; ETag: string }

type PublishedFields =
  { Title: string
    Excerpt: string
    Body: string
    Details: PostDetails }

/// `ETag` is the ETag of the working blob. A published post with no pending edit has none.
type PostDocument =
  { Slug: string
    State: PostState
    PendingEdit: bool
    PublishDate: string option
    ETag: string option
    Title: string
    Excerpt: string
    Body: string
    Details: PostDetails
    Text: string
    Published: PublishedFields option }

type CreatedDraft = { Slug: string; ETag: string }

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

type Device =
  { Id: string
    Label: string
    CreatedDate: string
    LastSeenDate: string }

type Rebuild =
  { Dispatch: string
    ProductionRun: int option }

type Rebuilt =
  | Queued of productionRun: int
  | QueuedWithoutRunNumber
  | Logged

[<RequireQualifiedAccess>]
type Severity =
  | Error
  | Warning

type ProblemPlace =
  | Field of name: string
  /// A line of the body text in the editor, from 1.
  | BodyLine of line: int

/// `Place` is `None` for a problem about the whole post.
type Problem =
  { Severity: Severity
    Place: ProblemPlace option
    Message: string }

type PreviewArticle = { Content: string; Outline: string }

/// `Article` is `None` when the text has an error in its parse.
type PostPreview =
  { Article: PreviewArticle option
    Problems: Problem list }

type Check =
  | Found of Problem list
  /// The API saved the post and could not check it.
  | CheckFailed

type SavedPost = { ETag: string; Check: Check }

type PublishCheck = { Problems: Problem list }

type PublishTime =
  | PublishNow
  /// An ISO 8601 UTC time, for example `2026-11-02T13:00:00Z`.
  | PublishOn of iso: string

type UpdateDate =
  | KeepUpdateDate
  /// An ISO 8601 UTC time, for example `2026-11-02T13:00:00Z`.
  | SetUpdateDate of iso: string

type PublishTarget =
  | Publish of PublishTime
  | PublishChanges of UpdateDate

type PublishWarning =
  | CommentRowMissing
  | PendingEditKept
  | NewerTextKept
  | ProductionRebuildFailed
  | StagingRebuildFailed
  | SiteNotRebuilt
  | DeployNotTracked

/// `PublishDate` is an ISO 8601 UTC time. `State` is `Published` or `Upcoming`.
type PublishedPost =
  { PublishDate: string
    State: PostState
    Warnings: PublishWarning list }

type ChangeWarning =
  | PublishStep of PublishWarning
  | PendingDateNotUpdated
  | PostRowKept
  | RevisionsKept
  | CommentsKept

/// `PublishDate` is `None` after an unpublish. `ETag` is the ETag of the working blob, or `None` when the post has no working blob.
type UpcomingChange =
  { PublishDate: string option
    State: PostState
    ETag: string option
    Warnings: ChangeWarning list }

type DeletedPost = { Warnings: ChangeWarning list }

type SeriesWarning =
  | SeriesRebuild of PublishWarning
  /// The rename wrote the new record, and the record at the old slug stays.
  | OldRecordKept

type SavedSeries =
  { Slug: string
    ETag: string
    Warnings: SeriesWarning list }

/// The fields of the series form. `Image` is an https URL, or `None`.
type SeriesFields =
  { Slug: string
    Title: string
    Excerpt: string
    Image: string option }

/// `Name` names the revision in a restore. `PublishedAt` is an ISO 8601 UTC time.
type Revision =
  { Name: string
    PublishedAt: string
    Title: string option }

type RestoredRevision = { ETag: string }

type Failure =
  /// The API answered 401 or 403.
  | Refused
  /// The API answered 412: the stored text is newer than the text that the request started from.
  | Stale
  | Unanswered
  | Invalid
  | Failed of reason: string

[<RequireQualifiedAccess>]
type RunState =
  | Waiting
  | Running
  | Succeeded
  | Failed
  | Canceled

type SiteRun =
  { Number: int
    State: RunState
    Url: string }

/// The latest run of each repo. The production run deploys the site of record.
type SiteDeploy =
  { Deploy: DeployState
    Production: SiteRun option
    Staging: SiteRun option }

let reason (failure: Failure) =
  match failure with
  | Refused -> "The API refused the token of this account."
  | Stale -> "The post changed in another place after this page loaded it."
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

let private deployState: Decoder<DeployState> =
  Decode.string
  |> Decode.andThen (fun name ->
    match name with
    | "deploying" -> Decode.succeed DeployState.Deploying
    | "deployed" -> Decode.succeed DeployState.Deployed
    | "failed" -> Decode.succeed DeployState.DeployFailed
    | _ -> Decode.fail $"The deploy state '{name}' is not known.")

let private runState: Decoder<RunState> =
  Decode.string
  |> Decode.andThen (fun name ->
    match name with
    | "waiting" -> Decode.succeed RunState.Waiting
    | "running" -> Decode.succeed RunState.Running
    | "succeeded" -> Decode.succeed RunState.Succeeded
    | "failed" -> Decode.succeed RunState.Failed
    | "canceled" -> Decode.succeed RunState.Canceled
    | _ -> Decode.fail $"The run state '{name}' is not known.")

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

let private severityName (severity: Severity) =
  match severity with
  | Severity.Error -> "error"
  | Severity.Warning -> "warning"

let private severity: Decoder<Severity> =
  Decode.string
  |> Decode.andThen (fun name ->
    match name with
    | "error" -> Decode.succeed Severity.Error
    | "warning" -> Decode.succeed Severity.Warning
    | _ -> Decode.fail $"The problem severity '{name}' is not known.")

let private problemPlace: Decoder<ProblemPlace> =
  Decode.field "kind" Decode.string
  |> Decode.andThen (fun kind ->
    match kind with
    | "field" -> Decode.field "field" Decode.string |> Decode.map Field
    | "body" -> Decode.field "line" Decode.int |> Decode.map BodyLine
    | _ -> Decode.fail $"The problem place '{kind}' is not known.")

let private problem: Decoder<Problem> =
  Decode.object (fun get ->
    { Severity = get.Required.Field "severity" severity
      Place = get.Optional.Field "place" problemPlace
      Message = get.Required.Field "message" Decode.string })

let private check: Decoder<Check> =
  Decode.field "kind" Decode.string
  |> Decode.andThen (fun kind ->
    match kind with
    | "found" -> Decode.field "problems" (Decode.list problem) |> Decode.map Found
    | "unavailable" -> Decode.succeed CheckFailed
    | _ -> Decode.fail $"The check state '{kind}' is not known.")

let private publishWarning: Decoder<PublishWarning> =
  Decode.string
  |> Decode.andThen (fun name ->
    match name with
    | "comment-row" -> Decode.succeed CommentRowMissing
    | "pending-edit" -> Decode.succeed PendingEditKept
    | "newer-text" -> Decode.succeed NewerTextKept
    | "production-rebuild" -> Decode.succeed ProductionRebuildFailed
    | "staging-rebuild" -> Decode.succeed StagingRebuildFailed
    | "site-rebuild" -> Decode.succeed SiteNotRebuilt
    | "deploy-tracking" -> Decode.succeed DeployNotTracked
    | _ -> Decode.fail $"The publish warning '{name}' is not known.")

let private changeWarning: Decoder<ChangeWarning> =
  Decode.string
  |> Decode.andThen (fun name ->
    match name with
    | "pending-date" -> Decode.succeed PendingDateNotUpdated
    | "post-row" -> Decode.succeed PostRowKept
    | "revisions" -> Decode.succeed RevisionsKept
    | "comments" -> Decode.succeed CommentsKept
    | _ -> publishWarning |> Decode.map PublishStep)

let private seriesPostState: Decoder<SeriesPostState> =
  Decode.string
  |> Decode.andThen (fun name ->
    match name with
    | "published" -> Decode.succeed SeriesPostState.Published
    | "upcoming" -> Decode.succeed SeriesPostState.Upcoming
    | "draft" -> Decode.succeed SeriesPostState.Draft
    | "pending-edit" -> Decode.succeed SeriesPostState.PendingEdit
    | _ -> Decode.fail $"The series post state '{name}' is not known.")

let private seriesWarning: Decoder<SeriesWarning> =
  Decode.string
  |> Decode.andThen (fun name ->
    match name with
    | "old-record" -> Decode.succeed OldRecordKept
    | _ -> publishWarning |> Decode.map SeriesRebuild)

let private coders =
  Extra.empty
  |> Extra.withCustom (fun (_: PostState) -> failwith "The admin sends no post state.") postState
  |> Extra.withCustom (fun (_: DeployState) -> failwith "The admin sends no deploy state.") deployState
  |> Extra.withCustom (fun (_: RunState) -> failwith "The admin sends no run state.") runState
  |> Extra.withCustom (commenterStatusName >> Encode.string) commenterStatus
  |> Extra.withCustom (commentApprovalName >> Encode.string) commentApproval
  |> Extra.withCustom (deliveryTypeName >> Encode.string) deliveryType
  |> Extra.withCustom (severityName >> Encode.string) severity
  |> Extra.withCustom (fun (_: Problem) -> failwith "The admin sends no problem.") problem
  |> Extra.withCustom (fun (_: Check) -> failwith "The admin sends no check.") check
  |> Extra.withCustom (fun (_: PublishWarning) -> failwith "The admin sends no publish warning.") publishWarning
  |> Extra.withCustom (fun (_: ChangeWarning) -> failwith "The admin sends no change warning.") changeWarning
  |> Extra.withCustom (fun (_: SeriesPostState) -> failwith "The admin sends no series post state.") seriesPostState
  |> Extra.withCustom (fun (_: SeriesWarning) -> failwith "The admin sends no series warning.") seriesWarning

type private Payload =
  | Json of string
  | Binary of Browser.Types.File

let inline private request<'T>
  (method: HttpMethod)
  (api: string)
  (token: string)
  (path: string)
  (body: Payload option)
  (preconditions: HttpRequestHeaders list)
  : JS.Promise<Result<ApiResponse<'T>, Failure>> =
  fetchUnsafe $"{api}/api/{path}" [
    Method method
    requestHeaders (
      [ Accept "application/json"; Authorization $"Bearer {token}" ]
      @ preconditions
      @ (match body with
         | Some(Json _) -> [ ContentType "application/json" ]
         | _ -> [])
    )
    match body with
    | Some(Json text) -> Body(U3.Case3 text)
    | Some(Binary file) -> Body(U3.Case1 file)
    | None -> ()
  ]
  |> Promise.bind (fun response ->
    if response.Status = 401 || response.Status = 403 then
      Promise.lift (Error Refused)
    elif response.Status = 412 then
      Promise.lift (Error Stale)
    else
      response.text ()
      |> Promise.map (fun text ->
        match Decode.Auto.fromString<ApiResponse<'T>> (text, caseStrategy = CamelCase, extra = coders) with
        | Ok { Success = false; Message = Some reason } -> Error(Failed reason)
        | Ok answer -> Ok answer
        | Error _ -> Error Invalid))
  |> Promise.catch (fun _ -> Error Unanswered)

let inline private sendWith<'T>
  (method: HttpMethod)
  (api: string)
  (token: string)
  (path: string)
  (body: Payload option)
  (preconditions: HttpRequestHeaders list)
  : JS.Promise<Result<'T, Failure>> =
  request<'T> method api token path body preconditions
  |> Promise.map (
    Result.bind (fun answer ->
      match answer with
      | { Success = true; Data = Some data } -> Ok data
      | _ -> Error Invalid)
  )

let inline private send<'T> (method: HttpMethod) (api: string) (token: string) (path: string) : JS.Promise<Result<'T, Failure>> =
  sendWith<'T> method api token path None []

let private act (method: HttpMethod) (api: string) (token: string) (path: string) (body: string option) =
  request<unit> method api token path (Option.map Json body) []
  |> Promise.map (
    Result.bind (fun answer ->
      match answer with
      | { Success = true } -> Ok()
      | _ -> Error Invalid)
  )

let private internalPath (segments: string list) =
  segments |> List.map JS.encodeURIComponent |> String.concat "/" |> (+) "internal/"

let private commentsPath (slug: string) = internalPath [ "posts"; slug; "comments" ]

let private commentPath (slug: string) (comment: Comment) =
  internalPath [ "posts"; slug; "comments"; comment.Id ]

let getPosts (api: string) (token: string) : JS.Promise<Result<Post list, Failure>> =
  send HttpMethod.GET api token (internalPath [ "posts" ])

let getPostDeploy (api: string) (token: string) (slug: string) : JS.Promise<Result<DeployState, Failure>> =
  send<PostDeploy> HttpMethod.GET api token (internalPath [ "posts"; slug; "deploy" ])
  |> Promise.map (Result.map _.Deploy)

let getSiteDeploy (api: string) (token: string) : JS.Promise<Result<SiteDeploy, Failure>> =
  send HttpMethod.GET api token (internalPath [ "deploys" ])

let rebuildSite (api: string) (token: string) : JS.Promise<Result<Rebuilt, Failure>> =
  send<Rebuild> HttpMethod.POST api token (internalPath [ "deploys" ])
  |> Promise.map (
    Result.bind (fun rebuild ->
      match rebuild.Dispatch, rebuild.ProductionRun with
      | "queued", Some run -> Ok(Queued run)
      | "queued", None -> Ok QueuedWithoutRunNumber
      | "logged", None -> Ok Logged
      | _ -> Error Invalid)
  )

let getModerationQueue (api: string) (token: string) : JS.Promise<Result<PostedComment list, Failure>> =
  send HttpMethod.GET api token (internalPath [ "comments" ])

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

let private commenterPath (commenterId: string) = internalPath [ "commenters"; commenterId ]

let getCommenters (api: string) (token: string) : JS.Promise<Result<Commenter list, Failure>> =
  send HttpMethod.GET api token (internalPath [ "commenters" ])

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
  send HttpMethod.GET api token (internalPath [ "subscriptions" ])

let private subscriptionPath (subscriptionId: string) = internalPath [ "subscriptions"; subscriptionId ]

let cancelSubscription (api: string) (token: string) (subscriptionId: string) : JS.Promise<Result<Subscription, Failure>> =
  send HttpMethod.POST api token (internalPath [ "subscriptions"; subscriptionId; "cancellations" ])

let deleteSubscription (api: string) (token: string) (subscriptionId: string) : JS.Promise<Result<unit, Failure>> =
  act HttpMethod.DELETE api token (subscriptionPath subscriptionId) None

let getSubscriptionDeliveries
  (api: string)
  (token: string)
  (subscriptionId: string)
  : JS.Promise<Result<Delivery list, Failure>> =
  send HttpMethod.GET api token (internalPath [ "subscriptions"; subscriptionId; "deliveries" ])

let getDevices (api: string) (token: string) : JS.Promise<Result<Device list, Failure>> =
  send HttpMethod.GET api token (internalPath [ "notifications"; "devices" ])

let registerDevice (api: string) (token: string) (keys: PushKeys) : JS.Promise<Result<Device, Failure>> =
  let body =
    Encode.object [
      "endpoint", Encode.string keys.endpoint
      "p256dh", Encode.string keys.p256dh
      "auth", Encode.string keys.auth
    ]
    |> Encode.toString 0

  request<Device> HttpMethod.POST api token (internalPath [ "notifications"; "devices" ]) (Some(Json body)) []
  |> Promise.map (
    Result.bind (fun answer ->
      match answer with
      | { Success = true; Data = Some device } -> Ok device
      | _ -> Error Invalid)
  )

let deleteDevice (api: string) (token: string) (deviceId: string) : JS.Promise<Result<unit, Failure>> =
  act HttpMethod.DELETE api token (internalPath [ "notifications"; "devices"; deviceId ]) None

let private postPath (slug: string) = internalPath [ "posts"; slug ]

let getPost (api: string) (token: string) (slug: string) : JS.Promise<Result<PostDocument, Failure>> =
  send HttpMethod.GET api token (postPath slug)

let createDraft (api: string) (token: string) : JS.Promise<Result<CreatedDraft, Failure>> =
  sendWith HttpMethod.POST api token (internalPath [ "posts" ]) (Some(Json "{}")) []

let private encodeDetails (details: PostDetails) =
  let optional (value: string option) =
    match value with
    | Some value -> Encode.string value
    | None -> Encode.nil

  Encode.object [
    "author", optional details.Author
    "category", optional details.Category
    "tags", details.Tags |> List.map Encode.string |> Encode.list
    "series", optional details.Series
    "image", optional details.Image
    "showOnHomepage", Encode.bool details.ShowOnHomepage
  ]

/// `eTag` is the ETag that the page read. `None` means that the post had no working blob.
let savePost
  (api: string)
  (token: string)
  (slug: string)
  (eTag: string option)
  (title: string)
  (excerpt: string)
  (body: string)
  (details: PostDetails)
  : JS.Promise<Result<SavedPost, Failure>> =
  let precondition =
    match eTag with
    | Some eTag -> IfMatch eTag
    | None -> IfNoneMatch "*"

  let json =
    Encode.object [
      "title", Encode.string title
      "excerpt", Encode.string excerpt
      "body", Encode.string body
      "details", encodeDetails details
    ]
    |> Encode.toString 0

  sendWith HttpMethod.PUT api token (postPath slug) (Some(Json json)) [ precondition ]

let getTerms (api: string) (token: string) : JS.Promise<Result<Terms, Failure>> =
  send HttpMethod.GET api token (internalPath [ "terms" ])

let getSeries (api: string) (token: string) : JS.Promise<Result<SeriesListing list, Failure>> =
  send HttpMethod.GET api token (internalPath [ "series" ])

let getSeriesRecords (api: string) (token: string) : JS.Promise<Result<SeriesRecord list, Failure>> =
  send HttpMethod.GET api token (internalPath [ "series" ])

/// `slug` is the slug of the stored record, or the slug of a new series. `eTag` is `None` for a new series.
let saveSeries
  (api: string)
  (token: string)
  (slug: string)
  (eTag: string option)
  (fields: SeriesFields)
  : JS.Promise<Result<SavedSeries, Failure>> =
  let json =
    Encode.object [
      "slug", Encode.string fields.Slug
      "title", Encode.string fields.Title
      "excerpt", Encode.string fields.Excerpt
      "image", (fields.Image |> Option.map Encode.string |> Option.defaultValue Encode.nil)
    ]
    |> Encode.toString 0

  let precondition =
    match eTag with
    | Some eTag -> IfMatch eTag
    | None -> IfNoneMatch "*"

  sendWith HttpMethod.PUT api token (internalPath [ "series"; slug ]) (Some(Json json)) [ precondition ]

let deleteSeries (api: string) (token: string) (slug: string) (eTag: string) : JS.Promise<Result<unit, Failure>> =
  request<unit> HttpMethod.DELETE api token (internalPath [ "series"; slug ]) None [ IfMatch eTag ]
  |> Promise.map (
    Result.bind (fun answer ->
      match answer with
      | { Success = true } -> Ok()
      | _ -> Error Invalid)
  )

let changeSlug (api: string) (token: string) (slug: string) (eTag: string) (newSlug: string) : JS.Promise<Result<ChangedSlug, Failure>> =
  let json = Encode.object [ "slug", Encode.string newSlug ] |> Encode.toString 0
  sendWith HttpMethod.PUT api token (internalPath [ "posts"; slug; "slug" ]) (Some(Json json)) [ IfMatch eTag ]

let previewPost
  (api: string)
  (token: string)
  (slug: string)
  (title: string)
  (excerpt: string)
  (body: string)
  (details: PostDetails)
  : JS.Promise<Result<PostPreview, Failure>> =
  let json =
    Encode.object [
      "title", Encode.string title
      "excerpt", Encode.string excerpt
      "body", Encode.string body
      "details", encodeDetails details
    ]
    |> Encode.toString 0

  sendWith HttpMethod.POST api token (internalPath [ "posts"; slug; "preview" ]) (Some(Json json)) []
let private publishBody (target: PublishTarget) =
  let name, date =
    match target with
    | Publish(PublishOn iso) -> "publishDate", Encode.string iso
    | Publish PublishNow -> "publishDate", Encode.nil
    | PublishChanges(SetUpdateDate iso) -> "updateDate", Encode.string iso
    | PublishChanges KeepUpdateDate -> "updateDate", Encode.nil

  Encode.object [ name, date ] |> Encode.toString 0

let private publishPath (slug: string) (target: PublishTarget) (last: string list) =
  match target with
  | Publish _ -> internalPath ([ "posts"; slug; "publish" ] @ last)
  | PublishChanges _ -> internalPath ([ "posts"; slug; "changes"; "publish" ] @ last)

let checkPublish
  (api: string)
  (token: string)
  (slug: string)
  (eTag: string)
  (target: PublishTarget)
  : JS.Promise<Result<PublishCheck, Failure>> =
  sendWith
    HttpMethod.POST
    api
    token
    (publishPath slug target [ "check" ])
    (Some(Json(publishBody target)))
    [ IfMatch eTag ]

let publishPost
  (api: string)
  (token: string)
  (slug: string)
  (eTag: string)
  (target: PublishTarget)
  : JS.Promise<Result<PublishedPost, Failure>> =
  sendWith
    HttpMethod.POST
    api
    token
    (publishPath slug target [])
    (Some(Json(publishBody target)))
    [ IfMatch eTag ]

let discardPending (api: string) (token: string) (slug: string) (eTag: string) : JS.Promise<Result<unit, Failure>> =
  request<unit> HttpMethod.POST api token (internalPath [ "posts"; slug; "changes"; "discard" ]) None [ IfMatch eTag ]
  |> Promise.map (
    Result.bind (fun answer ->
      match answer with
      | { Success = true } -> Ok()
      | _ -> Error Invalid)
  )

/// `eTag` is the ETag of the working blob that the page read. `None` means that the post had no working blob.
let workingPrecondition (eTag: string option) =
  match eTag with
  | Some eTag -> IfMatch eTag
  | None -> IfNoneMatch "*"

let unpublishPost (api: string) (token: string) (slug: string) (eTag: string option) : JS.Promise<Result<UpcomingChange, Failure>> =
  sendWith HttpMethod.POST api token (internalPath [ "posts"; slug; "unpublish" ]) (Some(Json "{}")) [ workingPrecondition eTag ]

/// `iso` is an ISO 8601 UTC time, for example `2026-11-02T13:00:00Z`.
let changePostDate
  (api: string)
  (token: string)
  (slug: string)
  (eTag: string option)
  (iso: string)
  : JS.Promise<Result<UpcomingChange, Failure>> =
  let json = Encode.object [ "publishDate", Encode.string iso ] |> Encode.toString 0
  sendWith HttpMethod.POST api token (internalPath [ "posts"; slug; "date" ]) (Some(Json json)) [ workingPrecondition eTag ]

/// `confirmSlug` is the text that the author typed, and `None` for a draft.
let deletePost
  (api: string)
  (token: string)
  (slug: string)
  (eTag: string option)
  (confirmSlug: string option)
  : JS.Promise<Result<DeletedPost, Failure>> =
  let confirmation =
    match confirmSlug with
    | Some typed -> Encode.string typed
    | None -> Encode.nil

  let json = Encode.object [ "confirmSlug", confirmation ] |> Encode.toString 0
  sendWith HttpMethod.DELETE api token (internalPath [ "posts"; slug ]) (Some(Json json)) [ workingPrecondition eTag ]

let getRevisions (api: string) (token: string) (slug: string) : JS.Promise<Result<Revision list, Failure>> =
  send HttpMethod.GET api token (internalPath [ "posts"; slug; "revisions" ])

let restoreRevision
  (api: string)
  (token: string)
  (slug: string)
  (eTag: string option)
  (revision: string)
  : JS.Promise<Result<RestoredRevision, Failure>> =
  let json = Encode.object [ "revision", Encode.string revision ] |> Encode.toString 0

  sendWith
    HttpMethod.POST
    api
    token
    (internalPath [ "posts"; slug; "revisions"; "restore" ])
    (Some(Json json))
    [ workingPrecondition eTag ]

type private UploadedAsset = { Url: string }

let uploadAsset (api: string) (token: string) (file: Browser.Types.File) : JS.Promise<Result<string, Failure>> =
  sendWith<UploadedAsset> HttpMethod.POST api token (internalPath [ "assets" ]) (Some(Binary file)) []
  |> Promise.map (Result.map (fun uploaded -> uploaded.Url))
