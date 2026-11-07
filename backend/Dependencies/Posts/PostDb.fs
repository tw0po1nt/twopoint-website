namespace TwoPoint.Dependencies.Posts

open TwoPoint.Dependencies.Util
open TwoPoint.Core.Shared
open TwoPoint.Core.Posts
open TwoPoint.Core.Util

open Azure.Data.Tables
open FsToolkit.ErrorHandling

open System
open System.Globalization

/// <summary>
/// Database representation of a post
/// </summary>
/// <remarks>
/// The <c>Post</c> entity has the following structure: <br/>
/// <c>
/// {
///    "PartitionKey": "&lt;slug&gt;",
///    "RowKey": "&lt;slug&gt;",
///    "Timestamp": "&lt;date&gt;",    
///    "CreatedDate": "&lt;date&gt;",
///    "Title": "&lt;string&gt;"
///  }
/// </c>
/// </remarks>
type internal DbPost =
  { Title : string
    Slug : string
    CreatedDate : DateTime }
  
  static member FromEntity (entity : TableEntity) = validation {
    let entityName = nameof DbPost
    let! title = "Title" |> entity.RequireString entityName
    let! createdDate =
      "CreatedDate"
      |> entity.RequireString entityName
      |> Validation.bind DateTime.TryValidateUtc

    return
      { Title = title
        Slug = entity.RowKey
        CreatedDate =  createdDate }
  }
  
  member this.ToEntity() =
    let entity = TableEntity(partitionKey = this.Slug, rowKey = this.Slug)
    entity["CreatedDate"] <- this.CreatedDate.ToString("o", CultureInfo.InvariantCulture)
    entity["Title"] <- this.Title
    entity
    
  static member FromPost (post : Post) =
    { Title = post.Info.Title |> NonEmptyString.value
      Slug = post.Id |> Slug.value
      CreatedDate = post.Info.CreatedDate }
    
  member this.ToPost (commentStats: PostCommentStats) =
    Post.create
      this.Slug
      this.Title
      (this.CreatedDate.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture))
      commentStats

/// <summary>
/// Database representation of a commenter
/// </summary>
/// <remarks>
/// The <c>Commenter</c> entity has the following structure: <br/>
/// <c>
/// {
///    "PartitionKey": "&lt;validationId&gt;",
///    "RowKey": "&lt;validationId&gt;",
///    "Timestamp": "&lt;date&gt;",
///    "EmailAddress" : "&lt;email&gt;"
///    "CreatedDate": "&lt;date&gt;",
///    "Name": "&lt;string&gt;" | null,
///    "Status": "&lt;new | trusted | banned&gt;"
///  }
/// </c>
/// </remarks>
type internal DbCommenter =
  { ValidationId : string
    EmailAddress : string
    Name : string option
    CreatedDate : DateTime
    Status : string }
  
  static member FromEntity (entity : TableEntity) = validation {
    let entityName = nameof DbCommenter
    let! emailAddress = "EmailAddress" |> entity.RequireString entityName
    let! createdDate =
      "CreatedDate"
      |> entity.RequireString entityName
      |> Validation.bind DateTime.TryValidateUtc
    let name = "Name" |> entity.TryGetString
    let! status = "Status" |> entity.RequireString entityName

    return
      { ValidationId = entity.RowKey
        EmailAddress = emailAddress
        Name = name
        CreatedDate = createdDate
        Status = status }
  }
  
  member this.ToEntity() =
    let entity = TableEntity(partitionKey = this.ValidationId, rowKey = this.ValidationId)
    entity["EmailAddress"] <- this.EmailAddress
    entity["CreatedDate"] <- this.CreatedDate.ToString("o", CultureInfo.InvariantCulture)
    entity["Name"] <- this.Name |> Option.toObj
    entity["Status"] <- this.Status
    entity

  static member FromCommenter (commenter : Commenter) =
    { ValidationId = commenter.ValidationId.ToString()
      EmailAddress = commenter.EmailAddress |> EmailAddress.value
      Name = commenter.Name |> Option.map NonEmptyString.value
      CreatedDate = commenter.CreatedDate
      Status = commenter.Status.ToString().ToLower() }

  member this.ToCommenter() =
    Commenter.create
      this.ValidationId
      this.EmailAddress
      this.Name
      (this.CreatedDate.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture))
      this.Status

/// <summary>
/// Database representation of a comment status
/// </summary>
/// <remarks>
/// The <c>CommentStatus</c> entity has the following structure: <br/>
/// <c>
///{
///    "PartitionKey": "&lt;comment rowKey&gt;",
///    "RowKey": "&lt;guid&gt;",
///    "Timestamp": "&lt;date&gt;",
///    "CreatedDate": "&lt;date&gt;",
///    "Approval": "&lt;new | approved | rejected&gt;",
///}
/// </c>
/// </remarks>
type internal DbCommentStatus =
  { Id : string
    CommentId : string
    CreatedDate : DateTime
    Approval : string }
  
  static member FromEntity (entity : TableEntity) = validation {
    let entityName = nameof DbCommentStatus
    let id = entity.RowKey
    let commentId = entity.PartitionKey
    let! createdDate =
      "CreatedDate"
      |> entity.RequireString entityName
      |> Validation.bind DateTime.TryValidateUtc
    let! approval = "Approval" |> entity.RequireString entityName

    return
      { Id = id
        CommentId = commentId
        CreatedDate = createdDate
        Approval = approval }
  }
  
  member this.ToEntity() =
    let entity = TableEntity(partitionKey = this.CommentId, rowKey = this.Id)
    entity["CreatedDate"] <- this.CreatedDate.ToString("o", CultureInfo.InvariantCulture)
    entity["Approval"] <- this.Approval
    entity
  
  member this.ToCommentStatus() =
    CommentStatus.create
      this.Approval
      (this.CreatedDate.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture))

[<RequireQualifiedAccess>]
type internal DbCommentWriter =
  | Commenter
  | Author

module internal DbCommentWriter =
  let ofEntityValue (value : string option) =
    match value with
    | None
    | Some "commenter" -> Validation.ok DbCommentWriter.Commenter
    | Some "author" -> Validation.ok DbCommentWriter.Author
    | Some other -> Validation.error $"DbComment.Writer must be 'commenter' or 'author', but was '{other}'"

  let toEntityValue writer =
    match writer with
    | DbCommentWriter.Commenter -> "commenter"
    | DbCommentWriter.Author -> "author"

/// <summary>
/// Database representation of a comment
/// </summary>
/// <remarks>
/// The <c>Comment</c> entity has the following structure: <br/>
/// <c>
///{
///    "PartitionKey": "&lt;post rowKey&gt;",
///    "RowKey": "&lt;guid&gt;",
///    "Timestamp": "&lt;date&gt;",
///    "CreatedDate": "&lt;date&gt;",
///    "Content": "&lt;string&gt;",
///    "CommenterId": "&lt;commenter rowKey&gt;" | "",
///    "ParentId": "&lt;comment rowKey&gt;" | null,
///    "Writer": "&lt;commenter | author&gt;" | null
///}
/// </c>
/// A missing <c>ParentId</c> is a top-level comment, and a missing <c>Writer</c> is a commenter.
/// <c>CommenterId</c> is empty when the writer is the author.
/// </remarks>
type internal DbComment =
  { Id : string
    PostId : string
    CreatedDate : DateTime
    Content : string
    CommenterId : string
    ParentId : string option
    Writer : DbCommentWriter }
  
  static member FromEntity (entity : TableEntity) = validation {
    let entityName = nameof DbComment
    let id = entity.RowKey
    let postId = entity.PartitionKey
    let! createdDate =
      "CreatedDate"
      |> entity.RequireString entityName
      |> Validation.bind DateTime.TryValidateUtc
    let! content = "Content" |> entity.RequireString entityName
    let! commenterId = "CommenterId" |> entity.RequireString entityName
    let parentId = "ParentId" |> entity.TryGetString
    let! writer = "Writer" |> entity.TryGetString |> DbCommentWriter.ofEntityValue

    return
      { Id = id
        PostId = postId
        CreatedDate = createdDate
        Content = content
        CommenterId = commenterId
        ParentId = parentId
        Writer = writer }
  }
  
  member this.ToEntity() =
    let entity = TableEntity(partitionKey = this.PostId, rowKey = this.Id)
    entity["CreatedDate"] <- this.CreatedDate.ToString("o", CultureInfo.InvariantCulture)
    entity["Content"] <- this.Content
    entity["CommenterId"] <- this.CommenterId
    entity["ParentId"] <- this.ParentId |> Option.toObj
    entity["Writer"] <- this.Writer |> DbCommentWriter.toEntityValue
    entity

  /// The commenter is None when the writer is the author.
  member this.ToComment(status: DbCommentStatus, commenter: DbCommenter option) = validation {
    let! writer =
      match this.Writer, commenter with
      | DbCommentWriter.Author, _ -> Validation.ok CommentWriter.Author
      | DbCommentWriter.Commenter, Some commenter ->
        commenter.ToCommenter() |> Validation.map CommentWriter.Commenter
      | DbCommentWriter.Commenter, None ->
        Validation.error $"No commenter with Id '{this.CommenterId}' for comment '{this.Id}'"

    return!
      Comment.create
        this.Id
        (this.CreatedDate.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture))
        {| Approval = status.Approval
           CreatedDate = status.CreatedDate.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) |}
        this.ParentId
        writer
        this.Content
  }

  /// A comment whose post is not in the post table gets the slug as its title, so that the author can
  /// still moderate it.
  member this.ToPostedComment(post: DbPost option, status: DbCommentStatus, commenter: DbCommenter option) = validation {
    let! slug = Slug.create this.PostId
    let! title =
      post
      |> Option.map _.Title
      |> Option.defaultValue this.PostId
      |> NonEmptyString.create (Some "title")
    let! comment = this.ToComment(status, commenter)
    return { PostedComment.Post = slug; PostTitle = title; Comment = comment }
  }


type internal IPostDb =
  abstract member GetAllPosts: unit -> CancellableTaskResult<DbPost list, DependencyError>
  abstract member GetPostBySlug: slug: string -> CancellableTaskResult<DbPost option, DependencyError>
  abstract member UpsertPost: post: DbPost -> CancellableTaskResult<DbPost, DependencyError>
  abstract member DeletePost: slug: string -> CancellableTaskResult<unit, DependencyError>
  abstract member HasCommentsOfPost: slug: string -> CancellableTaskResult<bool, DependencyError>
  abstract member DeleteCommentsOfPost: slug: string -> CancellableTaskResult<unit, DependencyError>
  
  abstract member GetAllCommenters: unit -> CancellableTaskResult<DbCommenter list, DependencyError>
  abstract member GetCommenterByEmailAddress: emailAddress: string -> CancellableTaskResult<DbCommenter option, DependencyError>
  abstract member GetCommenterByValidationId: validationId: string -> CancellableTaskResult<DbCommenter option, DependencyError>
  abstract member CreateCommenter: commenter: DbCommenter -> CancellableTaskResult<DbCommenter, DependencyError>
  abstract member UpdateCommenterStatus:
    commenter: DbCommenter
      -> status: string
      -> CancellableTaskResult<DbCommenter, DependencyError>
  abstract member DeleteCommenter: validationId: string -> CancellableTaskResult<unit, DependencyError>
  
  abstract member GetCommentsForPost:
    approvals: string list
      -> post: DbPost
      -> CancellableTaskResult<(DbComment * DbCommenter option * DbCommentStatus) list, DependencyError>
  abstract member GetCommentsOnAllPosts:
    approvals: string list
      -> CancellableTaskResult<(DbComment * DbCommenter option * DbCommentStatus) list, DependencyError>
  abstract member GetCommentsOfCommenter:
    approvals: string list
      -> commenter: DbCommenter
      -> CancellableTaskResult<(DbComment * DbCommenter option * DbCommentStatus) list, DependencyError>
  abstract member GetCommentById: commentId: string -> CancellableTaskResult<(DbComment * DbCommenter option * DbCommentStatus) option, DependencyError>
  abstract member CreateComment:
    comment: DbComment
      -> status: DbCommentStatus
      -> CancellableTaskResult<DbComment * DbCommenter option * DbCommentStatus, DependencyError>
  abstract member UpdateCommentStatus: status: DbCommentStatus -> CancellableTaskResult<DbCommentStatus, DependencyError>
  abstract member DeleteComment: commentId: string -> CancellableTaskResult<unit, DependencyError>


module internal PostDbError =
  let private postDbError =
    { Dependency = Dependency.Database
      Type = Unknown (exn "") }
    
  let validation msg = { postDbError with Type = DependencyErrorType.Validation msg }
    
  let unknown msg = { postDbError with Type = msg |> FSharp.Core.exn |> DependencyErrorType.Unknown }


module internal PostDb =
  
  open Azure.Data.Tables.FSharp
  open Microsoft.Extensions.Logging

  let withLatestStatus
    (logger : ILogger)
    approvals
    (dbComments : DbComment array)
    (commenters : DbCommenter list)
    (statuses : DbCommentStatus list)
    =
    let commenters = commenters |> List.toArray
    let statuses =
      statuses
      |> List.toArray
      |> Array.sortByDescending (fun (status: DbCommentStatus) -> status.CreatedDate)

    (Validation.ok [||], dbComments) ||> Array.fold (fun comments comment -> validation {
      let! comments = comments
      let commenter =
        match comment.Writer with
        | DbCommentWriter.Author -> Some None
        | DbCommentWriter.Commenter ->
          commenters
          |> Array.tryFind (fun ctr -> ctr.ValidationId = comment.CommenterId)
          |> Option.map Some

      match commenter with
      | None ->
        logger.LogWarning(
          "No commenter with Id '{commenterId}' for comment '{commentId}'. The comment is left out.",
          comment.CommenterId,
          comment.Id
        )
        return comments
      | Some commenter ->
        let! latestStat =
          statuses
          |> Array.filter (fun st -> st.CommentId = comment.Id)
          |> Array.tryHead
          |> Result.requireSome $"No status found for comment with Id '{comment.Id}'"

        if approvals |> List.contains latestStat.Approval
        then return [|(comment, commenter, latestStat)|] |> Array.append comments
        else return comments
    })
    |> DependencyError.ofValidation Dependency.Database
    |> Result.map (fun comments -> comments |> Array.sortBy (fun (comment, _, _) -> comment.CreatedDate))
    |> Result.map Array.toList

  let live (tableServiceClient : TableServiceClient) (logger : ILogger) =

    let db = nameof IPostDb

    let trySingle table onEntity partition key =
      Db.trySingle tableServiceClient logger db table onEntity partition key
      
    let trySingleQuery table onEntity partition query =
      Db.trySingleQuery tableServiceClient logger db table onEntity partition query
      
    let toList table onEntity =
      Db.toList tableServiceClient logger db table onEntity
      
    let where table onEntity filter =
      Db.where tableServiceClient logger db table onEntity filter

    let whereAnyKey table onEntity toFilter keys =
      Db.whereAnyKey tableServiceClient logger db table onEntity toFilter keys

    let add table onError onEntity entity =
      Db.add tableServiceClient logger db table onError onEntity entity
      
    let upsert table onError onEntity entity =
      Db.upsert tableServiceClient logger db table onError onEntity entity

    let update table onError onEntity entity updateEntity =
      Db.update tableServiceClient logger db table onError onEntity entity updateEntity
      
    let delete table onError filter =
      Db.delete tableServiceClient logger db table onError filter

    let deleteByKeys table onError partitionKey rowKey =
      Db.deleteByKeys tableServiceClient logger db table onError partitionKey rowKey
      
    let postsTable = "posts"
    let commentsTable = "comments"
    let commentStatusesTable = "commentStatuses"
    let commentersTable = "commenters"

    let getCommenterByValidationId validationId =
      validationId |> trySingle commentersTable DbCommenter.FromEntity (Some validationId)

    /// Some None for a comment of the author, and None for a comment whose commenter is missing.
    let commenterOf (comment : DbComment) =
      match comment.Writer with
      | DbCommentWriter.Author -> CancellableTaskResult.singleton (Some None)
      | DbCommentWriter.Commenter ->
        getCommenterByValidationId comment.CommenterId |> CancellableTaskResult.map (Option.map Some)

    { new IPostDb with
      
      member _.GetAllPosts () =
        toList postsTable DbPost.FromEntity
      
      member _.GetPostBySlug slug =
        slug |> trySingle postsTable DbPost.FromEntity (Some slug)

      member _.UpsertPost post =
        post.ToEntity() |> upsert postsTable PostDbError.validation DbPost.FromEntity

      member _.DeletePost slug =
        deleteByKeys postsTable PostDbError.validation slug slug

      member _.HasCommentsOfPost slug = cancellableTaskResult {
        let! (dbComments: DbComment list) = eq "PartitionKey" slug |> where commentsTable DbComment.FromEntity
        return not (List.isEmpty dbComments)
      }

      member _.DeleteCommentsOfPost slug = cancellableTaskResult {
        let! (dbComments: DbComment list) = eq "PartitionKey" slug |> where commentsTable DbComment.FromEntity

        for (comment: DbComment) in dbComments do
          do! eq "PartitionKey" comment.Id |> delete commentStatusesTable PostDbError.validation
          do! deleteByKeys commentsTable PostDbError.validation slug comment.Id
      }

      member _.GetAllCommenters () =
        toList commentersTable DbCommenter.FromEntity

      member _.GetCommenterByEmailAddress emailAddress =
        eq "EmailAddress" emailAddress |> trySingleQuery commentersTable DbCommenter.FromEntity None
      
      member _.GetCommenterByValidationId validationId = getCommenterByValidationId validationId

      member _.CreateCommenter commenter =
        commenter.ToEntity() |> add commentersTable PostDbError.validation DbCommenter.FromEntity
        
      member _.UpdateCommenterStatus commenter status =
        commenter.ToEntity() |> update commentersTable PostDbError.validation DbCommenter.FromEntity
        <| fun entity ->
          entity["Status"] <- status
          entity

      member _.DeleteCommenter validationId =
        deleteByKeys commentersTable PostDbError.validation validationId validationId
        
      member _.GetCommentsForPost approvals post = cancellableTaskResult {
        // Get comments
        let! dbComments = eq "PartitionKey" post.Slug |> where commentsTable DbComment.FromEntity
        let dbComments = dbComments |> List.toArray
      
        // for every comment, get the commenter
        let getCommenters =
          dbComments
          |> Array.filter (fun comment -> comment.Writer = DbCommentWriter.Commenter)
          |> Array.map _.CommenterId
          |> whereAnyKey commentersTable DbCommenter.FromEntity (fun commenterId ->
            eq "PartitionKey" commenterId + eq "RowKey" commenterId
          )

        // for every comment, get the comment statuses
        let getCommentStatuses =
          dbComments
          |> Array.map _.Id
          |> whereAnyKey commentStatusesTable DbCommentStatus.FromEntity (eq "PartitionKey")

        let! commenters, statuses = CancellableTaskResult.parallelZip getCommenters getCommentStatuses

        return! withLatestStatus logger approvals dbComments commenters statuses
      }

      member _.GetCommentsOnAllPosts approvals = cancellableTaskResult {
        let! dbComments = toList commentsTable DbComment.FromEntity
        let! commenters, statuses =
          CancellableTaskResult.parallelZip
            (toList commentersTable DbCommenter.FromEntity)
            (toList commentStatusesTable DbCommentStatus.FromEntity)

        return! withLatestStatus logger approvals (dbComments |> List.toArray) commenters statuses
      }

      member _.GetCommentsOfCommenter approvals commenter = cancellableTaskResult {
        let! dbComments = eq "CommenterId" commenter.ValidationId |> where commentsTable DbComment.FromEntity
        let dbComments = dbComments |> List.toArray
        let! statuses =
          dbComments
          |> Array.map _.Id
          |> whereAnyKey commentStatusesTable DbCommentStatus.FromEntity (eq "PartitionKey")

        return! withLatestStatus logger approvals dbComments [ commenter ] statuses
      }

      member _.GetCommentById commentId = cancellableTaskResult {
        // Get comment
        let! dbComment = commentId |> trySingle commentsTable DbComment.FromEntity None
        let! dbCommenter =
          dbComment
          |> Option.traverseCancellableTaskResult commenterOf
          |> CancellableTaskResult.map Option.flatten

        let! statuses =
          dbComment
          |> Option.traverseCancellableTaskResult (fun dbComment ->
            eq "PartitionKey" commentId |> where commentStatusesTable DbCommentStatus.FromEntity
          )
          |> CancellableTaskResult.map (Option.defaultValue [])
          
        let statuses =
          statuses
          |> List.toArray
          |> Array.sortByDescending (fun (status: DbCommentStatus) -> status.CreatedDate)
          
        let! dbCommentStatus =
          statuses
          |> Array.filter (fun st -> st.CommentId = commentId)
          |> Array.tryHead
          |> Result.requireSome (
            PostDbError.validation $"No status found for comment with Id '{commentId}'"
          )
          
        return option {
          let! comment = dbComment
          let! commenter = dbCommenter
          return comment, commenter, dbCommentStatus
        }
      }
      
      member _.CreateComment comment status = cancellableTaskResult {
        let! dbComment = comment.ToEntity() |> add commentsTable PostDbError.validation DbComment.FromEntity
        let! dbCommentStatus = status.ToEntity() |> add commentStatusesTable PostDbError.validation DbCommentStatus.FromEntity
        let! dbCommenter = commenterOf dbComment
        let! dbCommenter = dbCommenter |> Result.requireSome (
          PostDbError.validation $"Failed to fetch commenter for newly created comment with Id '{dbComment.Id}'"
        )
        return dbComment, dbCommenter, dbCommentStatus
      }
      
      member _.UpdateCommentStatus status =
        status.ToEntity() |> add commentStatusesTable PostDbError.validation DbCommentStatus.FromEntity
      
      member _.DeleteComment commentId = cancellableTaskResult {
        do! eq "PartitionKey" commentId |> delete commentStatusesTable PostDbError.validation
        do! eq "RowKey" commentId |> delete commentsTable PostDbError.validation
      }
    }
  
