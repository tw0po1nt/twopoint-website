namespace TwoPoint.Core.Posts

open TwoPoint.Core.Shared
open TwoPoint.Core.Util

open FsToolkit.ErrorHandling

open System

type CommenterStatus =
  | New
  | Trusted
  | Banned

  override this.ToString() =
    match this with
    | New -> "new"
    | Trusted -> "trusted"
    | Banned -> "banned"

module CommenterStatus =
  let create (value : string) =
    match value.ToLower() with
    | "new" -> Ok New
    | "trusted" -> Ok Trusted
    | "banned" -> Ok Banned
    | _ -> Error $"'{nameof CommenterStatus}' must be one of 'new', 'trusted', or 'banned'"

type CommenterValidationId =
  private | CommenterValidationId of Guid

  override this.ToString() =
    let (CommenterValidationId cvid) = this
    cvid.ToString()

module CommenterValidationId =
  let create =
    Constrained.String.tryParse
      Guid.TryParseOption
      $"'{nameof CommenterValidationId}' must be a valid guid"
      CommenterValidationId

  let createMaybe = Option.traverseResult create

type Commenter =
  { ValidationId : CommenterValidationId
    EmailAddress : EmailAddress
    Name : NonEmptyString option
    CreatedDate : DateTime
    Status : CommenterStatus }

module Commenter =
  let create validationId emailAddress name createdDate status = validation {
    let! validationId = CommenterValidationId.create validationId
    let! emailAddress = EmailAddress.create emailAddress
    let! name = NonEmptyString.createMaybe (Some (nameof name)) name
    let! createdDate = DateTime.TryValidateUtc(createdDate, (nameof createdDate))
    let! status = CommenterStatus.create status
    return
      { ValidationId = validationId
        Name = name
        EmailAddress = emailAddress
        CreatedDate = createdDate
        Status = status }
  }


type CommentId =
  private | CommentId of Guid

  override this.ToString() =
    let (CommentId cid) = this
    cid.ToString()

module CommentId =
  let create =
    Constrained.String.tryParse
      Guid.TryParseOption
      $"'{nameof CommentId}' must be a valid guid"
      CommentId


type CommentApproval =
  | New
  | Approved
  | Rejected

  override this.ToString() =
    match this with
    | New -> "new"
    | Approved -> "approved"
    | Rejected -> "rejected"

module CommentApproval =
  let create (value : string) =
    match value.ToLower() with
    | "new" -> Validation.ok New
    | "approved" -> Validation.ok Approved
    | "rejected" -> Validation.ok Rejected
    | _ -> Validation.error $"'{nameof CommentApproval}' must be one of 'new', 'approved', or 'rejected'"

  let all = [New; Approved; Rejected]


type CommentStatus =
  { Approval : CommentApproval
    CreatedDate : DateTime }

module CommentStatus =
  let create approval createdDate = validation {
    let! approval = CommentApproval.create approval
    let! createdDate = DateTime.TryValidateUtc(createdDate, (nameof createdDate))
    return { Approval = approval; CreatedDate = createdDate }
  }


type Comment =
  { Id : CommentId
    CreatedDate : DateTime
    Status : CommentStatus
    Commenter : Commenter
    Content : NonEmptyString }

module Comment =
  let create
    id
    createdDate
    (status:
      {| Approval : string
         CreatedDate : string
      |}
    )
    (commenter:
      {| ValidationId : string
         EmailAddress : string
         Name : string option
         CreatedDate : string
         Status : string
      |}
    )
    content =
      validation {
        let! id = CommentId.create id
        let! createdDate = DateTime.TryValidateUtc(createdDate, (nameof createdDate))
        let! status = CommentStatus.create status.Approval status.CreatedDate
        let! commenter =
          Commenter.create commenter.ValidationId commenter.EmailAddress commenter.Name commenter.CreatedDate commenter.Status
        let! content = NonEmptyString.create (Some (nameof content)) content
        return { Id = id; CreatedDate = createdDate; Status = status; Commenter = commenter; Content = content }
      }


type Slug =
  private | Slug of string

  override this.ToString() =
    let (Slug slug) = this
    slug

module Slug =

  let create =
    Constrained.String.tryParse
      (regexParser @"^([a-z0-9]+(-[a-z0-9]+)*)$")
      "Slug is invalid"
      Slug

  let createMaybe = Option.traverseResult create

  let value (Slug slug) = slug

  module Unsafe =
    let create = Types.unsafeCreate create


type PostCommentStats =
  { New : uint
    Approved : uint
    Rejected : uint }

  member this.Total = this.New + this.Approved + this.Rejected

  static member Zero = { New = 0u; Approved = 0u; Rejected = 0u }

  static member (+) (a: PostCommentStats, b: PostCommentStats) =
    { New = a.New + b.New; Approved = a.Approved + b.Approved; Rejected = a.Rejected + b.Rejected }

type PostInfo =
  { Slug : Slug
    Title : NonEmptyString
    CreatedDate : DateTime
    CommentStats : PostCommentStats }

module PostInfo =

  let create slug title createdDate commentStats = validation {
    let! slug = Slug.create slug
    let! title = title |> NonEmptyString.create (Some (nameof title))
    let! createdDate = DateTime.TryValidateUtc(createdDate, nameof createdDate)
    return { Slug = slug; Title = title;  CreatedDate = createdDate; CommentStats = commentStats }
  }

type PostState =
  | Draft
  | Published
  | Upcoming

/// The text of one blob in the post store.
type StoredPost = { Slug : Slug; Text : string; ETag : string }

/// The text of one blob in the post store, with the ETag that the store gave the blob.
type StoredBlob = { Text : string; ETag : string }

/// The store refuses a write when its condition is false.
type WriteCondition =
  | MatchesETag of string
  | MustNotExist

type WriteOutcome =
  | Written of eTag: string
  | ConditionFailed

type DeleteOutcome =
  | Deleted
  /// The blob changed since the caller read it, or it is gone.
  | DeleteConditionFailed

/// The text of one series record. The slug is the blob name without `.yml`.
type StoredSeries = { Slug : string; Text : string; ETag : string }

[<RequireQualifiedAccess>]
module StoredSeries =

  let parseOne (record : StoredSeries) : TwoPoint.Site.Render.Series option =
    TwoPoint.Site.Render.FrontMatter.parseSeries $"{record.Slug}.yml" record.Text |> Result.toOption

  /// A record that the parser cannot read is not in the list.
  let parse (records : StoredSeries list) : TwoPoint.Site.Render.Series list = records |> List.choose parseOne

/// A category or a tag on the published posts. `Value` is the text in the front matter of the newest published post
/// with the slug, and `Title` is the title that the site shows for it.
type TermCount = { Slug : Slug; Title : string; Value : string; Posts : int }

type TermSuggestions = { Categories : TermCount list; Tags : TermCount list }

type SeriesListing = { Slug : Slug; Title : string }

/// `Text` is the pending edit when the post has one, and else the published text. `ETag` is the ETag of the working blob.
type PostDocument =
  { Slug : Slug
    State : PostState
    HasPendingEdit : bool
    PublishDate : DateTime option
    ETag : string option
    Text : string
    PublishedText : string option }

type PostListing =
  { Slug : Slug
    Title : string
    State : PostState
    HasPendingEdit : bool
    PublishDate : DateTime option
    Deploy : TwoPoint.Core.Deploys.DeployState
    CommentStats : PostCommentStats }

type PostedComment =
  { Post : Slug
    PostTitle : NonEmptyString
    Comment : Comment }

type CommenterComments =
  { Commenter : Commenter
    Comments : PostedComment list }

type Post =
  { Id: Slug
    Info : PostInfo }

module Post =

  let create slug title createdDate commentStats = validation {
    let! id = Slug.create slug
    let! info = PostInfo.create slug title createdDate commentStats
    return { Id = id; Info = info }
  }
