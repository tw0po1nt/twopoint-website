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
        let publishDate = if hasPublished then summary.PublishDate |> Option.map _.UtcDateTime else None
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
// Get the document of a post
// ====================================================================================================================

[<RequireQualifiedAccess>]
module GetPostDocument =

  type Query = string -> CancellableTask<QueryResult<PostDocument option>>

  type Dependencies =
    abstract member GetUtcNow: unit -> DateTime
    abstract member GetPublishedBlob: slug: Slug -> DependencyResult<StoredBlob option>
    abstract member GetWorkingBlob: slug: Slug -> DependencyResult<StoredBlob option>

  [<RequireQualifiedAccess>]
  module Logic =

    let document
      (utcNow : DateTime)
      (slug : Slug)
      (published : StoredBlob option)
      (working : StoredBlob option)
      : PostDocument option =
      match published, working with
      | None, None -> None
      | _ ->
        let publishDate =
          published
          |> Option.bind (fun blob -> (TwoPoint.Site.Render.Post.summary blob.Text).PublishDate)
          |> Option.map _.UtcDateTime

        let state =
          match published, publishDate with
          | None, _ -> Draft
          | Some _, Some date when date > utcNow -> Upcoming
          | Some _, _ -> Published

        Some
          { Slug = slug
            State = state
            HasPendingEdit = published.IsSome && working.IsSome
            PublishDate = publishDate
            ETag = working |> Option.map _.ETag
            Text = (working |> Option.orElse published).Value.Text
            PublishedText = published |> Option.map _.Text }

  let withDependencies (dependencies : Dependencies) : Query =
    fun slug -> cancellableTaskResult {
      let! slug = slug |> Slug.create |> Result.mapError QueryError.Validation

      let! published, working =
        CancellableTaskResult.parallelZip (dependencies.GetPublishedBlob slug) (dependencies.GetWorkingBlob slug)
        |> CancellableTaskResult.mapError QueryError.Dependency

      return Logic.document (dependencies.GetUtcNow()) slug published working
    }

// ====================================================================================================================
// Get the terms of the published posts
// ====================================================================================================================

[<RequireQualifiedAccess>]
module GetTerms =

  type Query = unit -> CancellableTask<QueryResult<TermSuggestions>>

  type Dependencies =
    abstract member GetPublishedPosts: unit -> DependencyResult<StoredPost list>

  [<RequireQualifiedAccess>]
  module Logic =

    let private counts (titles : string list list) : TermCount list =
      titles
      |> List.collect (List.distinctBy TwoPoint.Site.Render.Slug.term)
      |> List.groupBy TwoPoint.Site.Render.Slug.term
      // A term slug can contain `_`, and a post slug cannot, so a term with such a slug is not a suggestion.
      |> List.choose (fun (slug, group) ->
        Slug.create slug
        |> Result.toOption
        // The input is in newest-first order, so the first title is the title of the newest post.
        |> Option.map (fun slug ->
          { Slug = slug
            Title = TwoPoint.Site.Render.Slug.termTitle group.Head
            Value = group.Head
            Posts = group.Length }))
      |> List.sortBy (fun term -> -term.Posts, term.Title)

    let suggestions (published : StoredPost list) : TermSuggestions =
      let newestFirst =
        published
        |> List.map (fun post ->
          TwoPoint.Site.Render.Post.summary post.Text |> _.PublishDate, TwoPoint.Site.Render.PostText.details post.Text)
        |> List.sortByDescending fst
        |> List.map snd

      let categories =
        newestFirst
        |> List.map (fun details -> Option.toList details.Category)
        |> counts
        |> List.filter (fun term -> Slug.value term.Slug <> TwoPoint.Site.Render.Rules.seriesCategory)

      let tags =
        newestFirst
        |> List.map _.Tags
        |> counts

      { Categories = categories; Tags = tags }

  let withDependencies (dependencies : Dependencies) : Query =
    fun () -> cancellableTaskResult {
      let! published =
        dependencies.GetPublishedPosts()
        |> CancellableTaskResult.mapError QueryError.Dependency

      return Logic.suggestions published
    }

// ====================================================================================================================
// Get the series
// ====================================================================================================================

[<RequireQualifiedAccess>]
module GetSeries =

  type Query = unit -> CancellableTask<QueryResult<SeriesListing list>>

  type Dependencies =
    abstract member GetSeriesRecords: unit -> DependencyResult<StoredSeries list>

  [<RequireQualifiedAccess>]
  module Logic =

    let listing (records : StoredSeries list) : SeriesListing list =
      StoredSeries.parse records
      |> List.choose (fun series ->
        Slug.create series.Slug
        |> Result.toOption
        |> Option.map (fun slug -> { Slug = slug; Title = series.Title }))
      |> List.sortBy (fun series -> series.Title.ToLowerInvariant(), Slug.value series.Slug)

  let withDependencies (dependencies : Dependencies) : Query =
    fun () -> cancellableTaskResult {
      let! records =
        dependencies.GetSeriesRecords()
        |> CancellableTaskResult.mapError QueryError.Dependency

      return Logic.listing records
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
