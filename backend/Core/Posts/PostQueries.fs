namespace TwoPoint.Core.Posts.Queries

open TwoPoint.Core.Posts
open TwoPoint.Core.Util

open FsToolkit.ErrorHandling
open IcedTasks

// ====================================================================================================================
// Get all posts
// ====================================================================================================================

type GetAllPosts = unit -> CancellableTask<QueryResult<PostInfo list>>

[<RequireQualifiedAccess>]
module GetAllPosts =

  type Dependencies =
    abstract member GetAllPosts: unit -> DependencyResult<PostInfo list>

  let withDependencies (dependencies : Dependencies) : GetAllPosts =
    fun () -> cancellableTaskResult {
      let! allPosts =
        dependencies.GetAllPosts()
        |> CancellableTaskResult.mapError QueryError.Dependency

      return allPosts
    }

// ====================================================================================================================
// Get post by slug
// ====================================================================================================================

type GetPostBySlug = string -> CancellableTask<QueryResult<Post option>>

[<RequireQualifiedAccess>]
module GetPostBySlug =

  type Dependencies =
    abstract member GetPostBySlug: slug: Slug -> DependencyResult<Post option>

  let withDependencies (dependencies : Dependencies) : GetPostBySlug =
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

type Approvals = string list
type Slug = string

type GetCommentsForPost =
  Approvals
    -> Slug
    -> CancellableTask<QueryResult<Comment list option>>

[<RequireQualifiedAccess>]
module GetCommentsForPost =

  type Dependencies =
    abstract member GetPostBySlug: slug: TwoPoint.Core.Posts.Slug -> DependencyResult<Post option>
    abstract member GetCommentsForPost:
      approvals: CommentApproval list
        -> post: Post
        -> DependencyResult<Comment list>

  let withDependencies (dependencies : Dependencies) : GetCommentsForPost =
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
    }
