namespace TwoPoint.Core.Posts.Queries

open TwoPoint.Core.Posts
open TwoPoint.Core.Util

open FsToolkit.ErrorHandling
open IcedTasks

open System

// ====================================================================================================================
// Get all posts
// ====================================================================================================================

[<RequireQualifiedAccess>]
module GetAllPosts =

  type Query = unit -> CancellableTask<QueryResult<PostListing list>>

  type Dependencies =
    abstract member GetUtcNow: unit -> DateTime
    abstract member GetPublishedPosts: unit -> DependencyResult<StoredPost list>
    abstract member GetWorkingPosts: unit -> DependencyResult<StoredPost list>
    abstract member GetPostsInTable: unit -> DependencyResult<PostInfo list>

  [<RequireQualifiedAccess>]
  module Logic =

    let private summaries (posts : StoredPost list) =
      posts |> List.map (fun post -> post.Slug, TwoPoint.Site.Render.Post.summary post.Text) |> Map.ofList

    let list
      (utcNow : DateTime)
      (published : StoredPost list)
      (working : StoredPost list)
      (table : PostInfo list)
      : PostListing list =
      let published = summaries published
      let working = summaries working
      let stats slug =
        table
        |> List.tryFind (fun info -> info.Slug = slug)
        |> Option.map _.CommentStats
        |> Option.defaultValue PostCommentStats.Zero

      Set.union (published |> Map.keys |> Set.ofSeq) (working |> Map.keys |> Set.ofSeq)
      |> Seq.map (fun slug ->
        let summary, hasPublished =
          match Map.tryFind slug published with
          | Some summary -> summary, true
          | None -> working[slug], false
        let publishDate = summary.PublishDate |> Option.map _.UtcDateTime
        let state =
          match hasPublished, publishDate with
          | false, _ -> Draft
          | true, Some date when date > utcNow -> Upcoming
          | true, _ -> Published

        { Slug = slug
          Title = summary.Title |> Option.defaultValue (Slug.value slug)
          State = state
          HasPendingEdit = hasPublished && working.ContainsKey slug
          PublishDate = publishDate
          CommentStats = stats slug })
      |> List.ofSeq
      |> List.sortBy (fun listing ->
        let newestFirst = listing.PublishDate |> Option.map (fun date -> -date.Ticks) |> Option.defaultValue Int64.MinValue
        newestFirst, Slug.value listing.Slug)

  let withDependencies (dependencies : Dependencies) : Query =
    fun () -> cancellableTaskResult {
      let! published, (working, table) =
        CancellableTaskResult.parallelZip
          (dependencies.GetPublishedPosts())
          (CancellableTaskResult.parallelZip (dependencies.GetWorkingPosts()) (dependencies.GetPostsInTable()))
        |> CancellableTaskResult.mapError QueryError.Dependency

      return Logic.list (dependencies.GetUtcNow()) published working table
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
