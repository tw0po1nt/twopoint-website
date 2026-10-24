namespace TwoPoint.Core.Posts.Queries

open TwoPoint.Core.Posts
open TwoPoint.Core.Util

open FsToolkit.ErrorHandling
open IcedTasks

// ====================================================================================================================
// Get all posts
// ====================================================================================================================

[<RequireQualifiedAccess>]
module GetAllPosts =

  type Query = unit -> CancellableTask<QueryResult<PostInfo list>>

  type Dependencies =
    abstract member GetAllPosts: unit -> DependencyResult<PostInfo list>

  let withDependencies (dependencies : Dependencies) : Query =
    fun () -> cancellableTaskResult {
      let! allPosts =
        dependencies.GetAllPosts()
        |> CancellableTaskResult.mapError QueryError.Dependency

      return allPosts
    }

// ====================================================================================================================
// Get post by slug
// ====================================================================================================================

[<RequireQualifiedAccess>]
module GetPostBySlug =

  type Query = string -> CancellableTask<QueryResult<Post option>>

  type Dependencies =
    abstract member GetPostBySlug: slug: Slug -> DependencyResult<Post option>

  let withDependencies (dependencies : Dependencies) : Query =
    fun slug -> cancellableTaskResult {
      let! slug =
        slug
        |> Slug.create
        |> Result.mapError QueryError.Validation

      let! post =
        dependencies.GetPostBySlug slug
        |> CancellableTaskResult.mapError QueryError.Dependency

      return post
    }

// ====================================================================================================================
// Get comments for post
// ====================================================================================================================

// Input

type Approvals = string list
type Slug = string

[<RequireQualifiedAccess>]
module GetCommentsForPost =

  type Query =
    Approvals
      -> Slug
      -> CancellableTask<QueryResult<Comment list option>>

  type Dependencies =
    abstract member GetPostBySlug: slug: TwoPoint.Core.Posts.Slug -> DependencyResult<Post option>
    abstract member GetCommentsForPost:
      approvals: CommentApproval list
        -> post: Post
        -> DependencyResult<Comment list>

  let withDependencies (dependencies : Dependencies) : Query =
    fun approvals slug -> cancellableTaskResult {
      let! slug =
        slug
        |> Slug.create
        |> Result.mapError QueryError.Validation
      let! post =
        dependencies.GetPostBySlug slug
        |> CancellableTaskResult.mapError QueryError.Dependency
      let! approvals =
        approvals
        |> List.map CommentApproval.create
        |> List.sequenceValidationA
        |> Result.mapError QueryError.Validation
        |> Result.map (List.defaultIfEmpty CommentApproval.all)
      return! 
        post
        |> Option.traverseCancellableTaskResult (dependencies.GetCommentsForPost approvals)
        |> CancellableTaskResult.mapError QueryError.Dependency
        |> CancellableTaskResult.map (Option.map (List.sortBy _.CreatedDate))
    }

// ====================================================================================================================
// Get moderation queue
// ====================================================================================================================

[<RequireQualifiedAccess>]
module GetModerationQueue =

  type Query = unit -> CancellableTask<QueryResult<PostedComment list>>

  type Dependencies =
    abstract member GetCommentsOnAllPosts:
      approvals: CommentApproval list
        -> DependencyResult<PostedComment list>

  let withDependencies (dependencies : Dependencies) : Query =
    fun () ->
      dependencies.GetCommentsOnAllPosts [ CommentApproval.New ]
      |> CancellableTaskResult.mapError QueryError.Dependency
      |> CancellableTaskResult.map (List.sortBy _.Comment.CreatedDate)

// ====================================================================================================================
// Get all commenters
// ====================================================================================================================

[<RequireQualifiedAccess>]
module GetAllCommenters =

  type Query = unit -> CancellableTask<QueryResult<Commenter list>>

  type Dependencies =
    abstract member GetAllCommenters: unit -> DependencyResult<Commenter list>

  let withDependencies (dependencies : Dependencies) : Query =
    fun () ->
      dependencies.GetAllCommenters()
      |> CancellableTaskResult.mapError QueryError.Dependency
      |> CancellableTaskResult.map (List.sortByDescending _.CreatedDate)

// ====================================================================================================================
// Get commenter
// ====================================================================================================================

[<RequireQualifiedAccess>]
module GetCommenter =

  type Query = string -> CancellableTask<QueryResult<CommenterComments option>>

  type Dependencies =
    abstract member GetCommenterByValidationId:
      validationId: CommenterValidationId
        -> DependencyResult<Commenter option>
    abstract member GetCommentsOfCommenter:
      commenter: Commenter
        -> DependencyResult<PostedComment list>

  let withDependencies (dependencies : Dependencies) : Query =
    fun validationId -> cancellableTaskResult {
      let! validationId =
        validationId
        |> CommenterValidationId.create
        |> Result.mapError QueryError.Validation
      let! commenter =
        dependencies.GetCommenterByValidationId validationId
        |> CancellableTaskResult.mapError QueryError.Dependency
      return!
        commenter
        |> Option.traverseCancellableTaskResult (fun commenter -> cancellableTaskResult {
          let! comments = dependencies.GetCommentsOfCommenter commenter
          return { Commenter = commenter; Comments = comments |> List.sortBy _.Comment.CreatedDate }
        })
        |> CancellableTaskResult.mapError QueryError.Dependency
    }
