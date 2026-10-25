namespace TwoPoint.Core.Posts.Actions

open TwoPoint.Core.Posts
open TwoPoint.Core.Shared
open TwoPoint.Core.Util

open FsToolkit.ErrorHandling
open IcedTasks

open System

// ====================================================================================================================
// Create post
// ====================================================================================================================

// Input

type NewPost =
  { Title : NonEmptyString
    Slug : Slug
    CreatedDate : DateTime }

module NewPost =
  
  type Title = Title of string
  type Slug = Slug of string
  type CreatedDate = CreatedDate of string

  type Dto =
    { Title : Title
      Slug : Slug
      CreatedDate : CreatedDate }
    
  let create (dto : Dto) = validation {
    let (Title title) = dto.Title
    let (Slug slug) = dto.Slug
    let (CreatedDate createdDate) = dto.CreatedDate
    let! title = title |> NonEmptyString.create (Some (nameof title))
    and! slug = Slug.create slug
    and! createdDate = DateTime.TryValidateUtc(createdDate, (nameof createdDate))
    
    return
      { NewPost.Title = title
        Slug = slug
        CreatedDate = createdDate }
  }
    
[<RequireQualifiedAccess>]
module CreatePost =
  
  // Output

  type Error = PostAlreadyExists of Post
  type Success = Created of NewPost
  type Decision = Decision<Success, Error>
  type Event = Created of Post
  
  // Logic
  
  type Logic =
    Post option
      -> NewPost
      -> Decision

  [<RequireQualifiedAccess>]
  module Logic =
    let decide : Logic =
      fun existingPost newPost -> result {
        do! existingPost |> Result.requireNoneWith PostAlreadyExists
        return Success.Created newPost
      }

  // Action
  
  type Action = NewPost.Dto -> CancellableTask<ActionResult<Event, Error>>
  
  type Dependencies =
    abstract member GetPostBySlug: slug: Slug -> DependencyResult<Post option>
    abstract member CreatePost: newPost: NewPost -> DependencyResult<Post>
    
  let withDependencies (dependencies : Dependencies) : Action =
    fun newPost -> cancellableTaskResult {
      let! newPost =
        newPost
        |> NewPost.create
        |> Result.mapError ActionError<Error>.Validation
                    
      let! existingPost =
        dependencies.GetPostBySlug newPost.Slug
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency
        
      let decision = Logic.decide existingPost newPost
        
      return!
        decision
        |> Result.map (fun (Success.Created newPost) -> newPost)
        |> Result.traverseCancellableTask dependencies.CreatePost
        |> CancellableTaskResult.foldResult
          (DependencyResult.toActionResult Event.Created)
          (Logic >> ActionResult.failure) 
    }

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

  type Event = Saved of eTag: string

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
      | Written eTag -> return Event.Saved eTag
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

type NewComment =
  { Post : Slug
    ValidationId : CommenterValidationId
    Content : NonEmptyString }

module NewComment =
  
  type Post = Post of string
  type ValidationId = ValidationId of string
  type Comment = Comment of string
  
  type Dto =
    { Post : Post
      ValidationId : ValidationId
      Comment : Comment }

  let create (dto : Dto) = validation {
    let (Post post) = dto.Post
    let (ValidationId validationId) = dto.ValidationId
    let (Comment content) = dto.Comment
    let! post = Slug.create post
    let! validationId = CommenterValidationId.create validationId
    let! content = NonEmptyString.create (Some (nameof content)) content
    return { Post = post; ValidationId = validationId; Content = content }
  }
  

[<RequireQualifiedAccess>]
module PostComment =
  
  // Output

  type Error =
    | NotFound of Data
    | Banned of Commenter
  and Data =
    | Post of Slug
    | Commenter of CommenterValidationId
  type Success = Posted of post: Post * comment: NewComment * commenter: Commenter * approval: CommentApproval
  type Decision = Decision<Success, Error>
  type Event = Posted of Post * Comment

  // Logic

  type Logic =
    Post option
      -> Commenter option
      -> NewComment
      -> Decision

  [<RequireQualifiedAccess>]
  module Logic =
    let decide : Logic =
      fun existingPost existingCommenter newComment -> result {
        let! post = existingPost |> Result.requireSome (newComment.Post |> Data.Post |> Error.NotFound)
        let! commenter = existingCommenter |> Result.requireSome (newComment.ValidationId |> Data.Commenter |> Error.NotFound)
        let! approval =
          match commenter.Status with
          | CommenterStatus.New -> Ok CommentApproval.New
          | CommenterStatus.Trusted -> Ok CommentApproval.Approved
          | CommenterStatus.Banned -> Error (Banned commenter)

        return Success.Posted (post, newComment, commenter, approval)
      }

  // Action

  type Action =
    NewComment.Dto
      -> CancellableTask<ActionResult<Event, Error>>

  type Dependencies =
    abstract member GetPostBySlug: slug: Slug -> DependencyResult<Post option>
    abstract member GetCommenterByValidationId: validationId: CommenterValidationId -> DependencyResult<Commenter option>
    abstract member CreateComment:
      post: Post
        -> commenter: Commenter
        -> newComment: NewComment
        -> approval: CommentApproval
        -> DependencyResult<Comment>
    abstract member QueueNewCommentNotice: commentId: CommentId -> DependencyResult<unit>

  let withDependencies (dependencies : Dependencies) : Action =

    let handleCommentPosted (Success.Posted (post, comment, commenter, approval)) : DependencyResult<Comment> =
      cancellableTaskResult {
        let! (created : Comment) = dependencies.CreateComment post commenter comment approval

        let! _ = dependencies.QueueNewCommentNotice created.Id |> CancellableTask.map ignore
        return created
      }

    fun newComment -> cancellableTaskResult {
      let! newComment =
        newComment
        |> NewComment.create
        |> Result.mapError ActionError<Error>.Validation

      let! existingPost =
        dependencies.GetPostBySlug newComment.Post
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let! existingCommenter =
        dependencies.GetCommenterByValidationId newComment.ValidationId
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let decision = Logic.decide existingPost existingCommenter newComment

      let commentPostedEvent existingPost comment = Event.Posted (existingPost, comment)

      return!
        decision
        |> Result.traverseCancellableTask handleCommentPosted
        |> CancellableTaskResult.foldResult
          (DependencyResult.toActionResult (commentPostedEvent (Option.get existingPost)))
          (Logic >> ActionResult.failure) 
    }
  
// ====================================================================================================================
// Update comment approval
// ====================================================================================================================

// Input

type CommentApprovalUpdate =
  { CommentId : CommentId
    Approval : CommentApproval }

module CommentApprovalUpdate =

  type CommentId = CommentId of string
  type Approval = Approval of string

  type Dto =
    { CommentId : CommentId 
      Approval : Approval }

  let create (dto : Dto) = validation {
    let (CommentId commentId) = dto.CommentId
    let (Approval approval) = dto.Approval
    let! commentId = CommentId.create commentId
    let! approval = CommentApproval.create approval
    return
      { CommentApprovalUpdate.CommentId = commentId
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
    abstract member GetCommentById:
      commentId: CommentId
        -> DependencyResult<Comment option>
    abstract member UpdateCommentApproval:
      comment: Comment
        -> approval: CommentApproval
        -> DependencyResult<Comment>

  let withDependencies (dependencies : Dependencies) : Action =

    let handleCommentApprovalUpdated (Success.Updated (comment, approval)) : DependencyResult<Comment> =
      dependencies.UpdateCommentApproval comment approval

    fun approvalUpdate -> cancellableTaskResult {
      let! (approvalUpdate : CommentApprovalUpdate) =
        approvalUpdate
        |> CommentApprovalUpdate.create
        |> Result.mapError ActionError<Error>.Validation

      let! existingComment =
        dependencies.GetCommentById approvalUpdate.CommentId
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let decision = Logic.decide existingComment approvalUpdate

      return!
        decision
        |> Result.traverseCancellableTask handleCommentApprovalUpdated
        |> CancellableTaskResult.foldResult
          (DependencyResult.toActionResult Event.Updated)
          (ActionError.Logic >> ActionResult.failure)
    }

// ====================================================================================================================
// Delete comment
// ====================================================================================================================

// Input

type CommentDeletion = { CommentId : CommentId }

module CommentDeletion =
 
  type CommentId = CommentId of string

  type Dto = { CommentId : CommentId }
  
  let create (dto : Dto) = validation {
    let (CommentId commentId) = dto.CommentId
    let! commentId = CommentId.create commentId
    return { CommentDeletion.CommentId = commentId }
  }

[<RequireQualifiedAccess>]
module DeleteComment =

  // Output

  type Error = CommentNotFound of CommentId
  type Success = Deleted of comment: Comment
  type Decision = Decision<Success, Error>
  type Event = Deleted of CommentId
  
  // Logic

  type Logic =
    Comment option
      -> CommentDeletion
      -> Decision

  [<RequireQualifiedAccess>]
  module Logic =
    let decide : Logic =
      fun existingComment deletion -> result {
        let! comment = existingComment |> Result.requireSome (Error.CommentNotFound deletion.CommentId)
        return Success.Deleted comment
      }

  // Action

  type Action = CommentDeletion.Dto -> CancellableTask<ActionResult<Event, Error>>

  type Dependencies =
    abstract member GetCommentById: commentId: CommentId -> DependencyResult<Comment option>
    abstract member DeleteComment: comment: Comment -> DependencyResult<unit>

  let withDependencies (dependencies : Dependencies) : Action =

    let handleCommentDeleted (Success.Deleted comment) : DependencyResult<CommentId> =
      cancellableTaskResult {
        do! dependencies.DeleteComment comment
        return comment.Id
      }

    fun commentDeletion -> cancellableTaskResult {
      let! (commentDeletion : CommentDeletion) =
        commentDeletion
        |> CommentDeletion.create
        |> Result.mapError ActionError<Error>.Validation

      let! existingComment =
        dependencies.GetCommentById commentDeletion.CommentId
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let decision = Logic.decide existingComment commentDeletion

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
  type Success = Deleted of commenter: Commenter * comments: Comment list
  type Decision = Decision<Success, Error>
  type Event = Deleted of Commenter

  // Logic

  type Logic =
    Commenter option
      -> Comment list
      -> CommenterDeletion
      -> Decision

  [<RequireQualifiedAccess>]
  module Logic =
    let decide : Logic =
      fun existingCommenter comments deletion -> result {
        let! commenter = existingCommenter |> Result.requireSome (CommenterNotFound deletion.ValidationId)
        return Success.Deleted (commenter, comments)
      }

  // Action

  type Action = CommenterDeletion.Dto -> CancellableTask<ActionResult<Event, Error>>

  type Dependencies =
    abstract member GetCommenterByValidationId: validationId: CommenterValidationId -> DependencyResult<Commenter option>
    abstract member GetCommentsOfCommenter: commenter: Commenter -> DependencyResult<Comment list>
    abstract member DeleteComment: comment: Comment -> DependencyResult<unit>
    abstract member DeleteCommenter: commenter: Commenter -> DependencyResult<unit>

  let withDependencies (dependencies : Dependencies) : Action =

    let deleteComments (comments : Comment list) : DependencyResult<unit> =
      cancellableTaskResult {
        for comment in comments do
          do! dependencies.DeleteComment comment
      }

    // The commenter goes after its comments, so that a second try after a failed delete finds the comments that are left.
    let handleCommenterDeleted (Success.Deleted (commenter, comments)) : DependencyResult<Commenter> =
      cancellableTaskResult {
        do! deleteComments comments
        do! dependencies.DeleteCommenter commenter
        // A comment that the commenter posted during the delete is not in the first read.
        let! postedDuringDelete = dependencies.GetCommentsOfCommenter commenter
        do! deleteComments postedDuringDelete
        return commenter
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
          dependencies.GetCommentsOfCommenter commenter
          |> CancellableTaskResult.mapError ActionError<Error>.Dependency
        | None -> CancellableTaskResult.singleton []

      let decision = Logic.decide existingCommenter comments commenterDeletion

      return!
        decision
        |> Result.traverseCancellableTask handleCommenterDeleted
        |> CancellableTaskResult.foldResult
          (DependencyResult.toActionResult Event.Deleted)
          (Logic >> ActionResult.failure)
    }
