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
  type Success = Posted of post: Post * comment: NewComment * commenter: Commenter
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
        do! commenter.Status.IsBanned |> Result.requireFalse (Banned existingCommenter.Value)

        return Success.Posted (post, newComment, commenter)
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
        -> DependencyResult<Comment>

  let withDependencies (dependencies : Dependencies) : Action =

    let handleCommentPosted (Success.Posted (post, comment, commenter)) : DependencyResult<Comment> =
      dependencies.CreateComment post commenter comment

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

  type Error = CommentNotFound of CommentId
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
    let decide : Logic =
      fun existingComment approvalUpdate -> result {
        let! comment = existingComment |> Result.requireSome (Error.CommentNotFound approvalUpdate.CommentId)
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
    abstract member UpdateCommenterStatus:
      commenter: Commenter
        -> status: CommenterStatus
        -> DependencyResult<Commenter>
    abstract member UpdateCommentApproval:
      comment: Comment
        -> approval: CommentApproval
        -> DependencyResult<Comment>

  let withDependencies (dependencies : Dependencies) : Action =

    let handleCommentApprovalUpdated (Success.Updated (comment, approval)) : DependencyResult<Comment> =
      cancellableTaskResult {
        do! cancellableTaskResult {
          match approval with
          | Approved ->
            return!
              CommenterStatus.Trusted
              |> dependencies.UpdateCommenterStatus comment.Commenter
              |> CancellableTaskResult.ignore
          | _ -> return ()
        }

        return!
          dependencies.UpdateCommentApproval comment approval
          |> CancellableTaskResult.map (fun comment ->
            match approval with
            | Approved -> { comment with Comment.Commenter.Status = CommenterStatus.Trusted }
            | _ -> comment
          )
      }

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

  type Error = CommenterNotFound of CommenterValidationId
  type Success = Updated of commenter: Commenter * status: CommenterStatus
  type Decision = Decision<Success, Error>
  type Event = Updated of Commenter

  // Logic

  type Logic =
    Commenter option
      -> CommenterStatusUpdate
      -> Decision

  [<RequireQualifiedAccess>]
  module Logic =
    let decide : Logic =
      fun existingCommenter statusUpdate -> result {
        let! commenter = existingCommenter |> Result.requireSome (CommenterNotFound statusUpdate.ValidationId)
        return Success.Updated (commenter, statusUpdate.Status)
      }

  // Action

  type Action =
    CommenterStatusUpdate.Dto
      -> CancellableTask<ActionResult<Event, Error>>

  type Dependencies =
    abstract member GetCommenterByValidationId: validationId: CommenterValidationId -> DependencyResult<Commenter option>
    abstract member UpdateCommenterStatus:
      commenter: Commenter
        -> status: CommenterStatus
        -> DependencyResult<Commenter>

  let withDependencies (dependencies : Dependencies) : Action =

    let handleCommenterStatusUpdated (Success.Updated (commenter, status)) : DependencyResult<Commenter> =
      cancellableTaskResult {
        // todo: call code to reject all comments from commenter if banned
        return! dependencies.UpdateCommenterStatus commenter status
      }

    fun statusUpdate -> cancellableTaskResult {
       let! (statusUpdate: CommenterStatusUpdate) =
         statusUpdate
         |> CommenterStatusUpdate.create
         |> Result.mapError ActionError<Error>.Validation

       let! existingCommenter =
         dependencies.GetCommenterByValidationId statusUpdate.ValidationId
         |> CancellableTaskResult.mapError ActionError<Error>.Dependency

       let decision = Logic.decide existingCommenter statusUpdate

       return!
         decision
         |> Result.traverseCancellableTask handleCommenterStatusUpdated
         |> CancellableTaskResult.foldResult
           (DependencyResult.toActionResult Event.Updated)
           (Logic >> ActionResult.failure)
    }
