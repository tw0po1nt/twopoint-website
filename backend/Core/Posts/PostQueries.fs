namespace TwoPoint.Core.Posts.Queries

open TwoPoint.Core.Deploys
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
    abstract member GetDeployContext: unit -> DependencyResult<DeployContext>
    abstract member GetMigratedPosts: unit -> DependencyResult<Set<Slug>>
    abstract member GetDeliveryStarts: unit -> DependencyResult<Map<Slug, DateTime>>

  [<RequireQualifiedAccess>]
  module Logic =

    let private summaries (posts : StoredPost list) =
      posts |> List.map (fun post -> post.Slug, TwoPoint.Site.Render.Post.summary post.Text) |> Map.ofList

    let list
      (utcNow : DateTime)
      (published : StoredPost list)
      (working : StoredPost list)
      (table : PostInfo list)
      (deploys : DeployContext)
      (migrated : Set<Slug>)
      (starts : Map<Slug, DateTime>)
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
        let deploy = DeployRule.stateOf utcNow deploys slug
        let deliveryToStart =
          match state, publishDate with
          | Published, Some date ->
            DeliveryGate.isDeliveryToStart
              utcNow
              date
              deploy
              (if migrated.Contains slug then Migrated else NotMigrated)
              (starts |> Map.tryFind slug |> Option.map Started |> Option.defaultValue NotStarted)
          | _ -> false

        { Slug = slug
          Title = summary.Title |> Option.defaultValue (Slug.value slug)
          State = state
          HasPendingEdit = hasPublished && working.ContainsKey slug
          PublishDate = publishDate
          Deploy = deploy
          DeliveryToStart = deliveryToStart
          CommentStats = stats slug })
      |> List.ofSeq
      |> List.sortBy (fun listing ->
        let newestFirst = listing.PublishDate |> Option.map (fun date -> -date.Ticks) |> Option.defaultValue Int64.MinValue
        newestFirst, Slug.value listing.Slug)

  let withDependencies (dependencies : Dependencies) : Query =
    fun () -> cancellableTaskResult {
      let! published, (working, (table, deploys)) =
        CancellableTaskResult.parallelZip
          (dependencies.GetPublishedPosts())
          (CancellableTaskResult.parallelZip
            (dependencies.GetWorkingPosts())
            (CancellableTaskResult.parallelZip (dependencies.GetPostsInTable()) (dependencies.GetDeployContext())))
        |> CancellableTaskResult.mapError QueryError.Dependency

      let! migrated, starts =
        CancellableTaskResult.parallelZip (dependencies.GetMigratedPosts()) (dependencies.GetDeliveryStarts())
        |> CancellableTaskResult.mapError QueryError.Dependency

      return Logic.list (dependencies.GetUtcNow()) published working table deploys migrated starts
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
    abstract member GetDeliveryStart: slug: Slug -> DependencyResult<DeliveryStart>

  [<RequireQualifiedAccess>]
  module Logic =

    let document
      (utcNow : DateTime)
      (slug : Slug)
      (published : StoredBlob option)
      (working : StoredBlob option)
      (start : DeliveryStart)
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
            PublishedText = published |> Option.map _.Text
            DeliveryStart = start }

  let withDependencies (dependencies : Dependencies) : Query =
    fun slug -> cancellableTaskResult {
      let! slug = slug |> Slug.create |> Result.mapError QueryError.Validation

      let! published, (working, start) =
        CancellableTaskResult.parallelZip
          (dependencies.GetPublishedBlob slug)
          (CancellableTaskResult.parallelZip (dependencies.GetWorkingBlob slug) (dependencies.GetDeliveryStart slug))
        |> CancellableTaskResult.mapError QueryError.Dependency

      return Logic.document (dependencies.GetUtcNow()) slug published working start
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

  type Dependencies = SeriesReads

  [<RequireQualifiedAccess>]
  module Logic =

    /// A record that the parser cannot read is not in the list.
    let listing (utcNow : DateTime) (store : SeriesStore) : SeriesListing list =
      store.Records
      |> List.choose (fun record ->
        match StoredSeries.parseOne record, Slug.create record.Slug with
        | Some series, Ok slug ->
          Some
            { Slug = slug
              Title = series.Title
              Excerpt = series.Excerpt
              Image = series.Image
              ETag = record.ETag
              Posts = SeriesPosts.inSeries utcNow store.Published store.Working slug }
        | _ -> None)
      |> List.sortBy (fun series -> series.Title.ToLowerInvariant(), Slug.value series.Slug)

  let withDependencies (dependencies : Dependencies) : Query =
    fun () -> cancellableTaskResult {
      let! store = SeriesPosts.read dependencies |> CancellableTaskResult.mapError QueryError.Dependency
      return Logic.listing (dependencies.GetUtcNow()) store
    }

// ====================================================================================================================
// Get the revisions of a post
// ====================================================================================================================

[<RequireQualifiedAccess>]
module GetRevisions =

  type Query = string -> CancellableTask<QueryResult<RevisionListing list>>

  type Dependencies =
    abstract member GetRevisionBlobs: slug: Slug -> DependencyResult<StoredRevision list>

  [<RequireQualifiedAccess>]
  module Logic =

    let listing (revisions : StoredRevision list) : RevisionListing list =
      revisions
      |> List.map (fun revision ->
        { Name = revision.Name
          PublishedAt = RevisionName.publishedAt revision.Name
          Title = (TwoPoint.Site.Render.Post.summary revision.Text).Title })
      |> List.sortByDescending _.PublishedAt

  let withDependencies (dependencies : Dependencies) : Query =
    fun slug -> cancellableTaskResult {
      let! slug = slug |> Slug.create |> Result.mapError QueryError.Validation

      let! revisions =
        dependencies.GetRevisionBlobs slug
        |> CancellableTaskResult.mapError QueryError.Dependency

      return Logic.listing revisions
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
      -> CancellableTask<QueryResult<AdminComment list option>>

  type Dependencies =
    abstract member GetPostBySlug: slug: TwoPoint.Core.Posts.Slug -> DependencyResult<Post option>
    abstract member GetCommentsForPost:
      approvals: CommentApproval list
        -> post: Post
        -> DependencyResult<Comment list>
    abstract member Stage: TwoPoint.Site.Render.Stage

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
        |> Option.traverseCancellableTaskResult (dependencies.GetCommentsForPost CommentApproval.all)
        |> CancellableTaskResult.mapError QueryError.Dependency
        |> CancellableTaskResult.map (
          Option.map (fun known ->
            known
            |> List.filter (fun comment -> approvals |> List.contains comment.Status.Approval)
            |> AdminComment.ofComments dependencies.Stage known)
        )
    }

// ====================================================================================================================
// Get public comments
// ====================================================================================================================

[<RequireQualifiedAccess>]
module GetPublicComments =

  type Query = Slug -> CancellableTask<QueryResult<PublicComment list option>>

  let withDependencies (dependencies : GetCommentsForPost.Dependencies) : Query =
    fun slug -> cancellableTaskResult {
      let! slug =
        slug
        |> Slug.create
        |> Result.mapError QueryError.Validation
      let! post =
        dependencies.GetPostBySlug slug
        |> CancellableTaskResult.mapError QueryError.Dependency
      return!
        post
        |> Option.traverseCancellableTaskResult (dependencies.GetCommentsForPost [ CommentApproval.Approved ])
        |> CancellableTaskResult.mapError QueryError.Dependency
        |> CancellableTaskResult.map (
          Option.map (
            CommentVisibility.visible
            >> List.sortBy _.CreatedDate
            >> List.map (RenderedComment.ofComment dependencies.Stage >> PublicComment.ofRendered)
          )
        )
    }

// ====================================================================================================================
// Get moderation queue
// ====================================================================================================================

[<RequireQualifiedAccess>]
module GetModerationQueue =

  type Query = unit -> CancellableTask<QueryResult<AdminPostedComment list>>

  type Dependencies =
    abstract member GetCommentsOnAllPosts:
      approvals: CommentApproval list
        -> DependencyResult<PostedComment list>
    abstract member Stage: TwoPoint.Site.Render.Stage

  let withDependencies (dependencies : Dependencies) : Query =
    fun () ->
      dependencies.GetCommentsOnAllPosts CommentApproval.all
      |> CancellableTaskResult.mapError QueryError.Dependency
      |> CancellableTaskResult.map (fun known ->
        known
        |> List.filter (fun posted -> posted.Comment.Status.Approval = CommentApproval.New)
        |> AdminPostedComment.ofPostedComments dependencies.Stage (known |> List.map _.Comment))

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
    abstract member GetComment: post: TwoPoint.Core.Posts.Slug -> commentId: CommentId -> DependencyResult<Comment option>
    abstract member GetRepliesOf: topLevels: PostedComment list -> DependencyResult<Comment list>
    abstract member Stage: TwoPoint.Site.Render.Stage

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
          let own = comments |> List.map (fun (posted : PostedComment) -> posted.Comment)
          let ownIds = own |> List.map _.Id |> Set.ofList

          // A reply is on the post of its top-level comment.
          let otherTopLevels =
            comments
            |> List.choose (fun (posted : PostedComment) -> posted.Comment.Parent |> Option.map (fun parent -> posted.Post, parent))
            |> List.distinct
            |> List.filter (snd >> ownIds.Contains >> not)

          let! topLevels =
            (CancellableTaskResult.singleton [], otherTopLevels)
            ||> List.fold (fun found (post, commentId) -> cancellableTaskResult {
              let! found = found
              let! topLevel = dependencies.GetComment post commentId
              return found @ Option.toList topLevel
            })

          let! replies = comments |> List.filter (fun posted -> posted.Comment.Parent.IsNone) |> dependencies.GetRepliesOf
          let known = own @ topLevels @ replies |> List.distinctBy _.Id

          return
            { Commenter = commenter
              Comments = AdminPostedComment.ofPostedComments dependencies.Stage known comments
              OtherReplies = (CommentDeleteRule.commenter own replies).Others }
        })
        |> CancellableTaskResult.mapError QueryError.Dependency
    }
