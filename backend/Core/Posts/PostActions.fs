namespace TwoPoint.Core.Posts.Actions

open TwoPoint.Core.Posts
open TwoPoint.Core.Shared
open TwoPoint.Core.Util

open FsToolkit.ErrorHandling
open IcedTasks

open System

type PostRow =
  { Title : NonEmptyString
    Slug : Slug
    CreatedDate : DateTime }

// ====================================================================================================================
// Create draft
// ====================================================================================================================

[<RequireQualifiedAccess>]
module DraftSlug =

  /// A term slug can contain `_`, and a post slug cannot.
  let ofTitle (title : string) =
    let slug =
      TwoPoint.Site.Render.Slug.term title
      |> fun term -> System.Text.RegularExpressions.Regex.Replace(term.Replace('_', '-'), "-+", "-")
      |> fun term -> term.Trim '-'

    if slug = "" then "untitled" else slug

  /// The slug itself, or else the first of `-2`, `-3`, and so on that no taken slug has.
  let free (taken : Set<string>) (slug : string) =
    Seq.initInfinite (fun index -> if index = 0 then slug else $"{slug}-{index + 1}")
    |> Seq.find (taken.Contains >> not)

[<RequireQualifiedAccess>]
module CreateDraft =

  type Dto = { Title : string }

  type Error = SlugTaken of Slug
  type Event = Created of Slug * eTag: string

  type Dependencies =
    abstract member GetPostSlugs: unit -> DependencyResult<Slug list>
    abstract member WriteWorkingBlob: slug: Slug -> text: string -> condition: WriteCondition -> DependencyResult<WriteOutcome>

  type Action = Dto -> CancellableTask<ActionResult<Event, Error>>

  let withDependencies (dependencies : Dependencies) : Action =
    fun dto -> cancellableTaskResult {
      let! taken =
        dependencies.GetPostSlugs()
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let! slug =
        DraftSlug.free (taken |> List.map Slug.value |> Set.ofList) (DraftSlug.ofTitle dto.Title)
        |> Slug.create
        |> Result.mapError ActionError<Error>.Validation

      let! text =
        TwoPoint.Site.Render.PostText.compose "" { Title = dto.Title.Trim(); Excerpt = ""; Body = "" }
        |> Result.mapError (List.singleton >> ActionError<Error>.Validation)

      let! outcome =
        dependencies.WriteWorkingBlob slug text MustNotExist
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      match outcome with
      | Written eTag -> return Event.Created(slug, eTag)
      | ConditionFailed -> return! Result.Error(ActionError.Logic(SlugTaken slug))
    }

// ====================================================================================================================
// Save draft
// ====================================================================================================================

[<RequireQualifiedAccess>]
module SaveDraft =

  /// `IfMatch` is the ETag of the working blob that the editor read. `None` means that the editor read no working blob.
  type Dto =
    { Slug : string
      IfMatch : string option
      Title : string
      Excerpt : string
      Body : string
      /// `None` leaves the details of the stored text as they are.
      Details : TwoPoint.Site.Render.PostText.Details option }

  type Error =
    | PostNotFound of Slug
    /// The YAML parser cannot read the front matter of the stored text. A save would drop the other fields.
    | FrontMatterUnreadable of reason: string
    /// The working blob changed after the editor read it, or the editor read a blob that no longer exists.
    | Stale

  type Event = Saved of eTag: string * check: PostCheck.Outcome

  [<RequireQualifiedAccess>]
  module Logic =

    /// The text to edit and the condition of the write. A first save of a published post edits the published text.
    let decide
      (slug : Slug)
      (published : StoredBlob option)
      (working : StoredBlob option)
      (ifMatch : string option)
      : Decision<string * WriteCondition, Error> =
      match published, working, ifMatch with
      | None, None, _ -> Decision.failure (PostNotFound slug)
      | _, Some blob, Some eTag when blob.ETag = eTag -> Decision.success (blob.Text, MatchesETag eTag)
      | _, Some _, _ -> Decision.failure Stale
      | _, None, Some _ -> Decision.failure Stale
      | Some blob, None, None -> Decision.success (blob.Text, MustNotExist)

  type Dependencies =
    abstract member GetPublishedBlob: slug: Slug -> DependencyResult<StoredBlob option>
    abstract member GetWorkingBlob: slug: Slug -> DependencyResult<StoredBlob option>
    abstract member WriteWorkingBlob: slug: Slug -> text: string -> condition: WriteCondition -> DependencyResult<WriteOutcome>
    abstract member Stage: TwoPoint.Site.Render.Stage
    abstract member Images: TwoPoint.Site.Render.IImageSize
    abstract member GetUtcNow: unit -> System.DateTime
    abstract member GetPublishedPosts: unit -> DependencyResult<StoredPost list>
    abstract member GetSeriesRecords: unit -> DependencyResult<StoredSeries list>
    abstract member ParsePublished: PostCheck.ParsePublished

  type Action = Dto -> CancellableTask<ActionResult<Event, Error>>

  let withDependencies (dependencies : Dependencies) : Action =
    fun dto -> cancellableTaskResult {
      let! slug = dto.Slug |> Slug.create |> Result.mapError ActionError<Error>.Validation

      let! published, working =
        CancellableTaskResult.parallelZip (dependencies.GetPublishedBlob slug) (dependencies.GetWorkingBlob slug)
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let! baseText, condition =
        Logic.decide slug published working dto.IfMatch
        |> Result.mapError ActionError<Error>.Logic

      let fields : TwoPoint.Site.Render.PostText.Fields = { Title = dto.Title; Excerpt = dto.Excerpt; Body = dto.Body }

      let composed =
        match dto.Details with
        | Some details -> TwoPoint.Site.Render.PostText.composeWithDetails baseText fields details
        | None -> TwoPoint.Site.Render.PostText.compose baseText fields

      let! text = composed |> Result.mapError (FrontMatterUnreadable >> ActionError<Error>.Logic)

      let! outcome =
        dependencies.WriteWorkingBlob slug text condition
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      match outcome with
      | Written eTag ->
        let kind = if published.IsSome then PostCheck.Published else PostCheck.Draft(dependencies.GetUtcNow())

        let! check =
          cancellableTask {
            try
              let! sources =
                CancellableTaskResult.parallelZip (dependencies.GetPublishedPosts()) (dependencies.GetSeriesRecords())

              match sources with
              | Ok(posts, records) ->
                let checkedPost =
                  PostCheck.forEditor
                    dependencies.Stage
                    dependencies.Images
                    kind
                    (dependencies.ParsePublished posts records)
                    slug
                    text

                return PostCheck.Problems checkedPost.Problems
              | Result.Error error -> return PostCheck.Unavailable(PostCheck.StoreUnavailable error)
            with error when not (error :? System.OperationCanceledException) ->
              return PostCheck.Unavailable(PostCheck.CheckCrashed error)
          }

        return Event.Saved(eTag, check)
      | ConditionFailed -> return! Result.Error(ActionError.Logic Stale)
    }

// ====================================================================================================================
// Change the slug of a draft
// ====================================================================================================================

[<RequireQualifiedAccess>]
module ChangeSlug =

  /// `IfMatch` is the ETag of the working blob that the editor read.
  type Dto =
    { Slug : string
      IfMatch : string option
      NewSlug : string }

  type Error =
    | PostNotFound of Slug
    /// The slug of a published post is fixed, because a public URL uses it.
    | PostPublished of Slug
    | SlugTaken of Slug
    | Stale

  type Event = Changed of Slug * eTag: string

  [<RequireQualifiedAccess>]
  module Logic =

    let decide
      (slug : Slug)
      (newSlug : Slug)
      (taken : Slug list)
      (published : StoredBlob option)
      (working : StoredBlob option)
      (ifMatch : string option)
      : Decision<StoredBlob, Error> =
      match published, working with
      | Some _, _ -> Decision.failure (PostPublished slug)
      | None, None -> Decision.failure (PostNotFound slug)
      | None, Some blob when ifMatch <> Some blob.ETag -> Decision.failure Stale
      | None, Some _ when newSlug <> slug && List.contains newSlug taken -> Decision.failure (SlugTaken newSlug)
      | None, Some blob -> Decision.success blob

  type Dependencies =
    abstract member GetPostSlugs: unit -> DependencyResult<Slug list>
    abstract member GetPublishedBlob: slug: Slug -> DependencyResult<StoredBlob option>
    abstract member GetWorkingBlob: slug: Slug -> DependencyResult<StoredBlob option>
    abstract member WriteWorkingBlob: slug: Slug -> text: string -> condition: WriteCondition -> DependencyResult<WriteOutcome>
    abstract member DeleteWorkingBlob: slug: Slug -> eTag: string -> DependencyResult<DeleteOutcome>

  type Action = Dto -> CancellableTask<ActionResult<Event, Error>>

  let withDependencies (dependencies : Dependencies) : Action =
    fun dto -> cancellableTaskResult {
      let! slug = dto.Slug |> Slug.create |> Result.mapError ActionError<Error>.Validation
      let! newSlug = dto.NewSlug |> Slug.create |> Result.mapError ActionError<Error>.Validation

      let! taken, (published, working) =
        CancellableTaskResult.parallelZip
          (dependencies.GetPostSlugs())
          (CancellableTaskResult.parallelZip (dependencies.GetPublishedBlob slug) (dependencies.GetWorkingBlob slug))
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let! (blob : StoredBlob) =
        Logic.decide slug newSlug taken published working dto.IfMatch
        |> Result.mapError ActionError<Error>.Logic

      if newSlug = slug then
        return Event.Changed(slug, blob.ETag)
      else
        let! written =
          dependencies.WriteWorkingBlob newSlug blob.Text MustNotExist
          |> CancellableTaskResult.mapError ActionError<Error>.Dependency

        match written with
        | ConditionFailed -> return! Result.Error(ActionError.Logic(SlugTaken newSlug))
        | Written newETag ->
          let! deleted =
            dependencies.DeleteWorkingBlob slug blob.ETag
            |> CancellableTaskResult.mapError ActionError<Error>.Dependency

          match deleted with
          | Deleted -> return Event.Changed(newSlug, newETag)
          | DeleteConditionFailed ->
            let! _ =
              dependencies.DeleteWorkingBlob newSlug newETag
              |> CancellableTaskResult.mapError ActionError<Error>.Dependency

            return! Result.Error(ActionError.Logic Stale)
    }

// ====================================================================================================================
// Validate commenter
// ====================================================================================================================

// Input

type CommenterValidation =
  { EmailAddress : EmailAddress
    Name : NonEmptyString option
    RedirectUri : Uri }

module CommenterValidation =
 
  type EmailAddress = EmailAddress of string
  type Name = Name of string
  type RedirectUri = RedirectUri of string
 
  type Dto =
    { EmailAddress : EmailAddress
      Name : Name option
      RedirectUri : RedirectUri }

  let create (dto : Dto) = validation {
    let (EmailAddress emailAddress) = dto.EmailAddress
    let name = dto.Name |> Option.map (fun (Name name) -> name)
    let (RedirectUri redirectUri) = dto.RedirectUri
    let! email = EmailAddress.create emailAddress
    let! name = NonEmptyString.createMaybe (Some (nameof name)) name
    let! redirectUri = Uri.TryValidate(redirectUri, nameof redirectUri)
    return
      { CommenterValidation.EmailAddress = email
        Name = name
        RedirectUri = redirectUri }
  }


[<RequireQualifiedAccess>]
module ValidateCommenter =

  // Output

  type Error =
    | Banned of Commenter
    | InvalidRedirect of Uri
  type Success = Validated of commenter: Commenter option * validation: CommenterValidation
  type Decision = Decision<Success, Error>
  type Event = Validated of Commenter
 
  // Logic

  type Logic =
    Commenter option
      -> Uri list
      -> CommenterValidation
      -> Decision

  [<RequireQualifiedAccess>]
  module Logic =
    let decide : Logic =
      fun existingCommenter validRedirectUris commenterValidation -> result {
        do! result {
          match existingCommenter with
          | Some commenter ->
            return! commenter.Status.IsBanned |> Result.requireFalse (Error.Banned existingCommenter.Value)
          | None ->
            return ()
        }

        do!
          validRedirectUris
          |> List.tryFind (fun uri -> uri.Host = commenterValidation.RedirectUri.Host)
          |> Result.requireSome (InvalidRedirect commenterValidation.RedirectUri)
          |> Result.ignore

        return Success.Validated (existingCommenter, commenterValidation)
      }

  // Action

  type Action =
    CommenterValidation.Dto
      -> CancellableTask<ActionResult<Event, Error>>

  type Dependencies =
    abstract member GetCommenterByEmailAddress: emailAddress: EmailAddress -> DependencyResult<Commenter option>
    abstract member GetValidRedirectUris: unit -> DependencyResult<Uri list>
    abstract member CreateCommenter:
      emailAddress: EmailAddress
        -> name: NonEmptyString option
        -> status: CommenterStatus
        -> DependencyResult<Commenter>
    abstract member SendCommenterValidationEmail:
      commenter: Commenter
        -> redirectUri: Uri
        -> DependencyResult<OperationId>
 
  let withDependencies (dependencies : Dependencies) : Action =
    let handleCommenterValidated (Success.Validated (commenter, commenterValidation)) : DependencyResult<Commenter> =
      cancellableTaskResult {
        let! (commenter: Commenter) = cancellableTaskResult {
          match commenter with
          | Some c -> return c
          | None -> return! dependencies.CreateCommenter commenterValidation.EmailAddress commenterValidation.Name CommenterStatus.New
        }

        do!
          dependencies.SendCommenterValidationEmail commenter commenterValidation.RedirectUri
          |> CancellableTaskResult.ignore

        return commenter
      }

    fun commenterValidation -> cancellableTaskResult {
      let! commenterValidation =
        commenterValidation
        |> CommenterValidation.create
        |> Result.mapError ActionError<Error>.Validation

      let! existingCommenter =
        dependencies.GetCommenterByEmailAddress commenterValidation.EmailAddress
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let! validRedirectUris =
        dependencies.GetValidRedirectUris()
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let decision = Logic.decide existingCommenter validRedirectUris commenterValidation

      return!
        decision
        |> Result.traverseCancellableTask handleCommenterValidated
        |> CancellableTaskResult.foldResult
          (DependencyResult.toActionResult Event.Validated)
          (Logic >> ActionResult.failure)
  }
    
// ====================================================================================================================
// Post comment
// ====================================================================================================================

// Input

[<RequireQualifiedAccess>]
type ReplyEmailsChoice =
  | Asked
  | NotAsked

[<RequireQualifiedAccess>]
type NewCommentTarget =
  | TopLevel of ReplyEmailsChoice
  | Reply of topLevel : CommentId

type NewComment =
  { Post : Slug
    ValidationId : CommenterValidationId
    Content : NonEmptyString
    Target : NewCommentTarget }

module NewComment =
  
  type Post = Post of string
  type ValidationId = ValidationId of string
  type Comment = Comment of string
  type ReplyTo = ReplyTo of string
  type ReplyEmails = ReplyEmails of bool
  
  /// `ReplyTo` is the ID of the top-level comment of a reply.
  type Dto =
    { Post : Post
      ValidationId : ValidationId
      Comment : Comment
      ReplyTo : ReplyTo option
      ReplyEmails : ReplyEmails }

  /// The `maxlength` of the comment form. Both count the source as typed, in UTF-16 code units.
  let maxLength = 1000

  let blankMessage = "content must contain a character other than a space or a new line"

  let replyEmailsOnReplyMessage = "replyEmails must be false for a reply, because a reply has no checkbox"

  let parseContent (content : string) =
    content
    |> Constrained.String.between (Some 1) (Some maxLength) (nameof content) id
    |> Result.bind (fun content -> if String.IsNullOrWhiteSpace content then Error [ blankMessage ] else Ok content)
    |> Result.bind (NonEmptyString.create (Some (nameof content)))

  let create (dto : Dto) = validation {
    let (Post post) = dto.Post
    let (ValidationId validationId) = dto.ValidationId
    let (Comment content) = dto.Comment
    let! post = Slug.create post
    let! validationId = CommenterValidationId.create validationId
    let! content = parseContent content
    and! target =
      match dto.ReplyTo, dto.ReplyEmails with
      | None, ReplyEmails true -> Ok (NewCommentTarget.TopLevel ReplyEmailsChoice.Asked)
      | None, ReplyEmails false -> Ok (NewCommentTarget.TopLevel ReplyEmailsChoice.NotAsked)
      | Some (ReplyTo replyTo), ReplyEmails false -> CommentId.create replyTo |> Result.map NewCommentTarget.Reply
      | Some _, ReplyEmails true -> Error [ replyEmailsOnReplyMessage ]
    return { Post = post; ValidationId = validationId; Content = content; Target = target }
  }


[<RequireQualifiedAccess>]
module PreviewComment =

  type Dto = { Content : string }

  let preview (stage : TwoPoint.Site.Render.Stage) (dto : Dto) : Result<string, string list> =
    NewComment.parseContent dto.Content
    |> Result.map (fun content -> TwoPoint.Site.Render.CommentBody.html stage (content.ToString()))


[<RequireQualifiedAccess>]
module PostComment =
  
  // Output

  type Error =
    | NotFound of Data
    | PostUpcoming of Post
    | ReplyRefused of ReplyRefusal
    | Banned of Commenter
  and Data =
    | Post of Slug
    | Commenter of CommenterValidationId
  type Success =
    | Posted of
      post: Post *
      comment: NewComment *
      topLevel: Comment option *
      commenter: Commenter *
      approval: CommentApproval *
      replyEmails: ReplyEmailConsent option
  type Decision = Decision<Success, Error>
  type Event = Posted of Post * RenderedComment

  // Logic

  type Logic =
    DateTime
      -> ReplyEmailStopToken
      -> Post option
      -> NewCommentPlace
      -> Commenter option
      -> NewComment
      -> Decision

  [<RequireQualifiedAccess>]
  module Logic =
    let decide : Logic =
      fun utcNow stopToken existingPost place existingCommenter newComment -> result {
        let! post = existingPost |> Result.requireSome (newComment.Post |> Data.Post |> Error.NotFound)

        let! topLevel =
          match place with
          | NewCommentPlace.TopLevel ->
            post.Info.CreatedDate <= utcNow |> Result.requireTrue (PostUpcoming post) |> Result.map (fun () -> None)
          | NewCommentPlace.Reply replyTo ->
            ReplyCheck.check utcNow post replyTo |> Result.map Some |> Result.mapError ReplyRefused

        let! commenter = existingCommenter |> Result.requireSome (newComment.ValidationId |> Data.Commenter |> Error.NotFound)
        let! approval =
          match commenter.Status with
          | CommenterStatus.New -> Ok CommentApproval.New
          | CommenterStatus.Trusted -> Ok CommentApproval.Approved
          | CommenterStatus.Banned -> Error (Banned commenter)

        let replyEmails =
          match newComment.Target with
          | NewCommentTarget.TopLevel ReplyEmailsChoice.Asked ->
            Some { ConsentDate = utcNow; StopToken = stopToken; StopDate = None }
          | NewCommentTarget.TopLevel ReplyEmailsChoice.NotAsked
          | NewCommentTarget.Reply _ -> None

        return Success.Posted (post, newComment, topLevel, commenter, approval, replyEmails)
      }

  // Action

  type Action =
    NewComment.Dto
      -> CancellableTask<ActionResult<Event, Error>>

  type Dependencies =
    abstract member GetPostBySlug: slug: Slug -> DependencyResult<Post option>
    abstract member GetUtcNow: unit -> DateTime
    abstract member GetPostedComment: post: Slug -> commentId: CommentId -> DependencyResult<PostedComment option>
    abstract member GetCommenterByValidationId: validationId: CommenterValidationId -> DependencyResult<Commenter option>
    abstract member NewCommentId: unit -> CommentId
    abstract member CreateComment:
      id: CommentId
        -> post: Post
        -> commenter: Commenter
        -> newComment: NewComment
        -> topLevel: Comment option
        -> approval: CommentApproval
        -> replyEmails: ReplyEmailConsent option
        -> DependencyResult<Comment>
    abstract member NewReplyEmailStopToken: unit -> ReplyEmailStopToken
    abstract member CreateReplyEmailStop: stop: ReplyEmailStop -> DependencyResult<unit>
    abstract member QueueNewCommentNotice: post: Slug -> commentId: CommentId -> DependencyResult<unit>
    abstract member QueueReplyEmail: post: Slug -> reply: CommentId -> DependencyResult<unit>
    abstract member Stage: TwoPoint.Site.Render.Stage

  let withDependencies (dependencies : Dependencies) : Action =

    let handleCommentPosted (Success.Posted (post, comment, topLevel, commenter, approval, replyEmails)) : DependencyResult<RenderedComment> =
      cancellableTaskResult {
        let id = dependencies.NewCommentId()

        do!
          replyEmails
          |> Option.traverseCancellableTaskResult (fun consent ->
            dependencies.CreateReplyEmailStop { Token = consent.StopToken; Post = post.Id; Comment = id })
          |> CancellableTaskResult.ignore

        let! (created : Comment) = dependencies.CreateComment id post commenter comment topLevel approval replyEmails

        let! _ = dependencies.QueueNewCommentNotice post.Id created.Id |> CancellableTask.map ignore
        do! QueueReplyEmail.atFirstApproval dependencies.QueueReplyEmail post.Id None created

        return RenderedComment.ofComment dependencies.Stage created
      }

    fun newComment -> cancellableTaskResult {
      let! newComment =
        newComment
        |> NewComment.create
        |> Result.mapError ActionError<Error>.Validation

      let! existingPost =
        dependencies.GetPostBySlug newComment.Post
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let! place =
        (match newComment.Target with
         | NewCommentTarget.TopLevel _ -> CancellableTaskResult.singleton NewCommentPlace.TopLevel
         | NewCommentTarget.Reply replyTo -> dependencies.GetPostedComment newComment.Post replyTo |> CancellableTaskResult.map NewCommentPlace.Reply)
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let! existingCommenter =
        dependencies.GetCommenterByValidationId newComment.ValidationId
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let decision =
        Logic.decide (dependencies.GetUtcNow()) (dependencies.NewReplyEmailStopToken()) existingPost place existingCommenter newComment

      let commentPostedEvent existingPost comment = Event.Posted (existingPost, comment)

      return!
        decision
        |> Result.traverseCancellableTask handleCommentPosted
        |> CancellableTaskResult.foldResult
          (DependencyResult.toActionResult (commentPostedEvent (Option.get existingPost)))
          (Logic >> ActionResult.failure) 
    }
  
// ====================================================================================================================
// Reply as the author
// ====================================================================================================================

// Input

/// `ReplyTo` is the comment of the row, a top-level comment or a reply.
type AuthorReply =
  { Post : Slug
    ReplyTo : CommentId
    Content : NonEmptyString }

module AuthorReply =

  type Dto =
    { Post : string
      ReplyTo : string
      Content : string }

  let create (dto : Dto) = validation {
    let! post = Slug.create dto.Post
    and! replyTo = CommentId.create dto.ReplyTo
    and! content = NewComment.parseContent dto.Content
    return ({ Post = post; ReplyTo = replyTo; Content = content } : AuthorReply)
  }

[<RequireQualifiedAccess>]
module ReplyAsAuthor =

  // Output

  type Error = Refused of AuthorReplyRefusal
  /// `Approved` is each comment that the reply approved, the top-level comment first.
  type Event = Replied of reply : RenderedComment * approved : Comment list

  // Logic

  [<RequireQualifiedAccess>]
  module Logic =
    let decide (reply : AuthorReply) (replyTo : PostedComment option) (topLevel : PostedComment option) =
      AuthorReplyRule.place reply.Post replyTo topLevel |> Result.mapError Refused

  // Action

  type Action = AuthorReply.Dto -> CancellableTask<ActionResult<Event, Error>>

  type Dependencies =
    abstract member GetPostedComment: post: Slug -> commentId: CommentId -> DependencyResult<PostedComment option>
    abstract member UpdateCommentApproval: comment: Comment -> approval: CommentApproval -> DependencyResult<Comment>
    abstract member CreateAuthorReply:
      post: Slug
        -> topLevel: Comment
        -> content: NonEmptyString
        -> approval: CommentApproval
        -> DependencyResult<Comment>
    abstract member QueueReplyEmail: post: Slug -> reply: CommentId -> DependencyResult<unit>
    abstract member Stage: TwoPoint.Site.Render.Stage

  let withDependencies (dependencies : Dependencies) : Action =

    // The approvals go first, so that the reply is never under a hidden comment.
    let handleReplyPlaced (reply : AuthorReply) (place : AuthorReplyPlace) : DependencyResult<Event> =
      cancellableTaskResult {
        let approved = ResizeArray<Comment>()

        for comment in place.Approvals do
          let! updated = dependencies.UpdateCommentApproval comment CommentApproval.Approved
          approved.Add updated
          do! QueueReplyEmail.atFirstApproval dependencies.QueueReplyEmail reply.Post (Some comment.Status.Approval) updated

        let approved = List.ofSeq approved
        let! created = dependencies.CreateAuthorReply reply.Post place.TopLevel reply.Content CommentApproval.Approved
        do! QueueReplyEmail.atFirstApproval dependencies.QueueReplyEmail reply.Post None created
        return Event.Replied(RenderedComment.ofComment dependencies.Stage created, approved)
      }

    fun dto -> cancellableTaskResult {
      let! reply = dto |> AuthorReply.create |> Result.mapError ActionError<Error>.Validation

      let! replyTo =
        dependencies.GetPostedComment reply.Post reply.ReplyTo
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let! topLevel =
        match replyTo |> Option.bind (fun (replyTo : PostedComment) -> replyTo.Comment.Parent) with
        | Some parent ->
          dependencies.GetPostedComment reply.Post parent
          |> CancellableTaskResult.mapError ActionError<Error>.Dependency
        | None -> CancellableTaskResult.singleton None

      let! place = Logic.decide reply replyTo topLevel |> Result.mapError ActionError<Error>.Logic

      return!
        handleReplyPlaced reply place
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency
    }

// ====================================================================================================================
// Update comment approval
// ====================================================================================================================

// Input

type CommentApprovalUpdate =
  { Post : Slug
    CommentId : CommentId
    Approval : CommentApproval }

module CommentApprovalUpdate =

  type Post = Post of string
  type CommentId = CommentId of string
  type Approval = Approval of string

  type Dto =
    { Post : Post
      CommentId : CommentId 
      Approval : Approval }

  let create (dto : Dto) = validation {
    let (Post post) = dto.Post
    let (CommentId commentId) = dto.CommentId
    let (Approval approval) = dto.Approval
    let! post = Slug.create post
    and! commentId = CommentId.create commentId
    and! approval = CommentApproval.create approval
    return
      { CommentApprovalUpdate.Post = post
        CommentId = commentId
        Approval = approval }
  }

[<RequireQualifiedAccess>]
module UpdateCommentApproval =

  // Output

  type Error =
    | CommentNotFound of CommentId
    | ChangeNotAllowed of current: CommentApproval * requested: CommentApproval
  type Success = Updated of comment: Comment * approval: CommentApproval
  type Decision = Decision<Success, Error>
  type Event = Updated of Comment

  // Logic

  type Logic =
    Comment option
      -> CommentApprovalUpdate
      -> Decision

  [<RequireQualifiedAccess>]
  module Logic =
    let isAllowed (current : CommentApproval) (requested : CommentApproval) =
      match current, requested with
      | CommentApproval.New, CommentApproval.Approved
      | CommentApproval.New, CommentApproval.Rejected
      | CommentApproval.Approved, CommentApproval.Rejected
      | CommentApproval.Rejected, CommentApproval.Approved -> true
      | _, CommentApproval.New
      | CommentApproval.Approved, CommentApproval.Approved
      | CommentApproval.Rejected, CommentApproval.Rejected -> false

    let decide : Logic =
      fun existingComment approvalUpdate -> result {
        let! comment = existingComment |> Result.requireSome (Error.CommentNotFound approvalUpdate.CommentId)
        let current = comment.Status.Approval
        do!
          isAllowed current approvalUpdate.Approval
          |> Result.requireTrue (ChangeNotAllowed (current, approvalUpdate.Approval))
        return Success.Updated (comment, approvalUpdate.Approval)
      }


  // Action

  type Action =
    CommentApprovalUpdate.Dto
      -> CancellableTask<ActionResult<Event, Error>>

  type Dependencies =
    abstract member GetComment:
      post: Slug
        -> commentId: CommentId
        -> DependencyResult<Comment option>
    abstract member UpdateCommentApproval:
      comment: Comment
        -> approval: CommentApproval
        -> DependencyResult<Comment>
    abstract member QueueReplyEmail: post: Slug -> reply: CommentId -> DependencyResult<unit>

  let withDependencies (dependencies : Dependencies) : Action =

    let handleCommentApprovalUpdated (post : Slug) (Success.Updated (comment, approval)) : DependencyResult<Comment> =
      cancellableTaskResult {
        let! updated = dependencies.UpdateCommentApproval comment approval
        do! QueueReplyEmail.atFirstApproval dependencies.QueueReplyEmail post (Some comment.Status.Approval) updated
        return updated
      }

    fun approvalUpdate -> cancellableTaskResult {
      let! (approvalUpdate : CommentApprovalUpdate) =
        approvalUpdate
        |> CommentApprovalUpdate.create
        |> Result.mapError ActionError<Error>.Validation

      let! existingComment =
        dependencies.GetComment approvalUpdate.Post approvalUpdate.CommentId
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let decision = Logic.decide existingComment approvalUpdate

      return!
        decision
        |> Result.traverseCancellableTask (handleCommentApprovalUpdated approvalUpdate.Post)
        |> CancellableTaskResult.foldResult
          (DependencyResult.toActionResult Event.Updated)
          (ActionError.Logic >> ActionResult.failure)
    }

// ====================================================================================================================
// Delete comment
// ====================================================================================================================

// Input

type CommentDeletion =
  { Post : Slug
    CommentId : CommentId }

module CommentDeletion =

  type Post = Post of string
  type CommentId = CommentId of string

  type Dto =
    { Post : Post
      CommentId : CommentId }

  let create (dto : Dto) = validation {
    let (Post post) = dto.Post
    let (CommentId commentId) = dto.CommentId
    let! post = Slug.create post
    and! commentId = CommentId.create commentId
    return { CommentDeletion.Post = post; CommentId = commentId }
  }

[<RequireQualifiedAccess>]
module DeleteComment =

  // Output

  type Error = CommentNotFound of CommentId
  type Success = Deleted of comment: PostedComment * removal: CommentRemoval
  type Decision = Decision<Success, Error>
  /// `others` is the number of replies that went with the comment.
  type Event = Deleted of CommentId * others: int

  // Logic

  type Logic =
    PostedComment option
      -> Comment list
      -> CommentDeletion
      -> Decision

  [<RequireQualifiedAccess>]
  module Logic =
    /// `replies` is each reply under the comment.
    let decide : Logic =
      fun existingComment replies deletion -> result {
        let! posted = existingComment |> Result.requireSome (Error.CommentNotFound deletion.CommentId)
        return Success.Deleted (posted, CommentDeleteRule.thread replies posted.Comment)
      }

  // Action

  type Action = CommentDeletion.Dto -> CancellableTask<ActionResult<Event, Error>>

  type Dependencies =
    abstract member GetPostedComment: post: Slug -> commentId: CommentId -> DependencyResult<PostedComment option>
    abstract member GetRepliesOf: topLevels: PostedComment list -> DependencyResult<Comment list>
    abstract member DeleteComment: comment: Comment -> DependencyResult<unit>

  let withDependencies (dependencies : Dependencies) : Action =

    let deleteComments (comments : Comment list) : DependencyResult<unit> =
      cancellableTaskResult {
        for comment in comments do
          do! dependencies.DeleteComment comment
      }

    let repliesOf (comments : PostedComment list) =
      comments |> List.filter (fun posted -> posted.Comment.Parent.IsNone) |> dependencies.GetRepliesOf

    let handleCommentDeleted (Success.Deleted (posted, removal)) : DependencyResult<CommentId * int> =
      cancellableTaskResult {
        do! deleteComments removal.Comments
        // A reply that a reader posted during the delete is not in the first read. Its count is not in `Others`.
        let! postedDuringDelete = repliesOf [ posted ]
        do! deleteComments (CommentDeleteRule.thread postedDuringDelete posted.Comment |> CommentRemoval.exceptDeleted removal)
        return posted.Comment.Id, removal.Others
      }

    fun commentDeletion -> cancellableTaskResult {
      let! (commentDeletion : CommentDeletion) =
        commentDeletion
        |> CommentDeletion.create
        |> Result.mapError ActionError<Error>.Validation

      let! existingComment =
        dependencies.GetPostedComment commentDeletion.Post commentDeletion.CommentId
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let! replies =
        existingComment
        |> Option.toList
        |> repliesOf
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let decision = Logic.decide existingComment replies commentDeletion

      return!
        decision
        |> Result.traverseCancellableTask handleCommentDeleted
        |> CancellableTaskResult.foldResult
          (DependencyResult.toActionResult Event.Deleted)
          (Logic >> ActionResult.failure)
    } 

// ====================================================================================================================
// Update commenter status
// ====================================================================================================================

// Input

type CommenterStatusUpdate =
  { ValidationId : CommenterValidationId
    Status : CommenterStatus }

module CommenterStatusUpdate =

  type ValidationId = ValidationId of string
  type Status = Status of string

  type Dto =
    { ValidationId : ValidationId 
      Status : Status }

  let create (dto : Dto) = validation {
    let (ValidationId validationId) = dto.ValidationId
    let (Status status) = dto.Status
    let! validationId = CommenterValidationId.create validationId
    let! status = CommenterStatus.create status
    return
      { CommenterStatusUpdate.ValidationId = validationId
        Status = status }
  }

[<RequireQualifiedAccess>]
module UpdateCommenterStatus =

  // Output

  type Error =
    | CommenterNotFound of CommenterValidationId
    | ChangeNotAllowed of current: CommenterStatus * requested: CommenterStatus
  type Success = Updated of commenter: Commenter * status: CommenterStatus * rejections: Comment list
  type Decision = Decision<Success, Error>
  type Event = Updated of Commenter

  // Logic

  type Logic =
    Commenter option
      -> Comment list
      -> CommenterStatusUpdate
      -> Decision

  [<RequireQualifiedAccess>]
  module Logic =
    let isAllowed (current : CommenterStatus) (requested : CommenterStatus) =
      match current, requested with
      | CommenterStatus.Banned, CommenterStatus.Trusted -> false
      | CommenterStatus.Banned, (CommenterStatus.New | CommenterStatus.Banned)
      | (CommenterStatus.New | CommenterStatus.Trusted), (CommenterStatus.New | CommenterStatus.Trusted | CommenterStatus.Banned) -> true

    let rejections (comments : Comment list) (requested : CommenterStatus) =
      match requested with
      | CommenterStatus.Banned -> comments |> List.filter (fun comment -> comment.Status.Approval = CommentApproval.New)
      | CommenterStatus.New
      | CommenterStatus.Trusted -> []

    let decide : Logic =
      fun existingCommenter comments statusUpdate -> result {
        let! commenter = existingCommenter |> Result.requireSome (CommenterNotFound statusUpdate.ValidationId)
        do!
          isAllowed commenter.Status statusUpdate.Status
          |> Result.requireTrue (ChangeNotAllowed (commenter.Status, statusUpdate.Status))
        return Success.Updated (commenter, statusUpdate.Status, rejections comments statusUpdate.Status)
      }

  // Action

  type Action =
    CommenterStatusUpdate.Dto
      -> CancellableTask<ActionResult<Event, Error>>

  type Dependencies =
    abstract member GetCommenterByValidationId: validationId: CommenterValidationId -> DependencyResult<Commenter option>
    abstract member GetCommentsOfCommenter: commenter: Commenter -> DependencyResult<Comment list>
    abstract member UpdateCommenterStatus:
      commenter: Commenter
        -> status: CommenterStatus
        -> DependencyResult<Commenter>
    abstract member UpdateCommentApproval:
      comment: Comment
        -> approval: CommentApproval
        -> DependencyResult<Comment>

  let withDependencies (dependencies : Dependencies) : Action =

    let handleCommenterStatusUpdated (Success.Updated (commenter, status, rejections)) : DependencyResult<Commenter> =
      cancellableTaskResult {
        let! commenter = dependencies.UpdateCommenterStatus commenter status
        for comment in rejections do
          do! dependencies.UpdateCommentApproval comment CommentApproval.Rejected |> CancellableTaskResult.ignore
        return commenter
      }

    fun statusUpdate -> cancellableTaskResult {
       let! (statusUpdate: CommenterStatusUpdate) =
         statusUpdate
         |> CommenterStatusUpdate.create
         |> Result.mapError ActionError<Error>.Validation

       let! existingCommenter =
         dependencies.GetCommenterByValidationId statusUpdate.ValidationId
         |> CancellableTaskResult.mapError ActionError<Error>.Dependency

       let! comments =
         match existingCommenter with
         | Some commenter ->
           dependencies.GetCommentsOfCommenter commenter
           |> CancellableTaskResult.mapError ActionError<Error>.Dependency
         | None -> CancellableTaskResult.singleton []

       let decision = Logic.decide existingCommenter comments statusUpdate

       return!
         decision
         |> Result.traverseCancellableTask handleCommenterStatusUpdated
         |> CancellableTaskResult.foldResult
           (DependencyResult.toActionResult Event.Updated)
           (Logic >> ActionResult.failure)
    }

// ====================================================================================================================
// Delete commenter
// ====================================================================================================================

// Input

type CommenterDeletion = { ValidationId : CommenterValidationId }

module CommenterDeletion =

  type ValidationId = ValidationId of string

  type Dto = { ValidationId : ValidationId }

  let create (dto : Dto) = validation {
    let (ValidationId validationId) = dto.ValidationId
    let! validationId = CommenterValidationId.create validationId
    return { CommenterDeletion.ValidationId = validationId }
  }

[<RequireQualifiedAccess>]
module DeleteCommenter =

  // Output

  type Error = CommenterNotFound of CommenterValidationId
  type Success = Deleted of commenter: Commenter * removal: CommentRemoval
  type Decision = Decision<Success, Error>
  /// `others` is the number of replies from other writers that went with the commenter.
  type Event = Deleted of Commenter * others: int

  // Logic

  type Logic =
    Commenter option
      -> Comment list
      -> Comment list
      -> CommenterDeletion
      -> Decision

  [<RequireQualifiedAccess>]
  module Logic =
    /// `replies` is each reply under a top-level comment of the commenter.
    let decide : Logic =
      fun existingCommenter comments replies deletion -> result {
        let! commenter = existingCommenter |> Result.requireSome (CommenterNotFound deletion.ValidationId)
        return Success.Deleted (commenter, CommentDeleteRule.commenter comments replies)
      }

  // Action

  type Action = CommenterDeletion.Dto -> CancellableTask<ActionResult<Event, Error>>

  type Dependencies =
    abstract member GetCommenterByValidationId: validationId: CommenterValidationId -> DependencyResult<Commenter option>
    abstract member GetPostedCommentsOfCommenter: commenter: Commenter -> DependencyResult<PostedComment list>
    abstract member GetRepliesOf: topLevels: PostedComment list -> DependencyResult<Comment list>
    abstract member DeleteComment: comment: Comment -> DependencyResult<unit>
    abstract member DeleteCommenter: commenter: Commenter -> DependencyResult<unit>

  let withDependencies (dependencies : Dependencies) : Action =

    let deleteComments (comments : Comment list) : DependencyResult<unit> =
      cancellableTaskResult {
        for comment in comments do
          do! dependencies.DeleteComment comment
      }

    let repliesOf (comments : PostedComment list) =
      comments |> List.filter (fun posted -> posted.Comment.Parent.IsNone) |> dependencies.GetRepliesOf

    // The commenter goes after its comments, so that a second try after a failed delete finds the comments that are left.
    let handleCommenterDeleted (firstRead : PostedComment list) (Success.Deleted (commenter, removal)) : DependencyResult<Commenter * int> =
      cancellableTaskResult {
        do! deleteComments removal.Comments
        do! dependencies.DeleteCommenter commenter
        // The first read and `Others` do not include a comment that someone posts during the delete.
        let! postedDuringDelete = dependencies.GetPostedCommentsOfCommenter commenter
        let own = firstRead @ postedDuringDelete |> List.distinctBy _.Comment.Id
        let! replies = repliesOf own
        do! deleteComments (CommentDeleteRule.commenter (own |> List.map _.Comment) replies |> CommentRemoval.exceptDeleted removal)
        return commenter, removal.Others
      }

    fun commenterDeletion -> cancellableTaskResult {
      let! (commenterDeletion : CommenterDeletion) =
        commenterDeletion
        |> CommenterDeletion.create
        |> Result.mapError ActionError<Error>.Validation

      let! existingCommenter =
        dependencies.GetCommenterByValidationId commenterDeletion.ValidationId
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let! comments =
        match existingCommenter with
        | Some commenter ->
          dependencies.GetPostedCommentsOfCommenter commenter
          |> CancellableTaskResult.mapError ActionError<Error>.Dependency
        | None -> CancellableTaskResult.singleton []

      let! replies =
        repliesOf comments
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let decision = Logic.decide existingCommenter (comments |> List.map _.Comment) replies commenterDeletion

      return!
        decision
        |> Result.traverseCancellableTask (handleCommenterDeleted comments)
        |> CancellableTaskResult.foldResult
          (DependencyResult.toActionResult Event.Deleted)
          (Logic >> ActionResult.failure)
    }
