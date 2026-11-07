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
/// The comment of a reply-email stop token
/// </summary>
/// <remarks>
/// The <c>ReplyEmailStop</c> entity has the token as its PartitionKey and its RowKey, and the properties
/// <c>PostId</c> and <c>CommentId</c>.
/// </remarks>
type internal DbReplyEmailStop =
  { Token : string
    PostId : string
    CommentId : string }

  static member FromEntity (entity : TableEntity) = validation {
    let entityName = nameof DbReplyEmailStop
    let! postId = "PostId" |> entity.RequireString entityName
    and! commentId = "CommentId" |> entity.RequireString entityName
    return { Token = entity.RowKey; PostId = postId; CommentId = commentId }
  }

  member this.ToEntity() =
    let entity = TableEntity(partitionKey = this.Token, rowKey = this.Token)
    entity["PostId"] <- this.PostId
    entity["CommentId"] <- this.CommentId
    entity

  static member OfStop (stop : ReplyEmailStop) =
    { Token = stop.Token.ToString()
      PostId = Slug.value stop.Post
      CommentId = stop.Comment.ToString() }

  member this.ToStop() = validation {
    let! token = ReplyEmailStopToken.create this.Token
    and! post = Slug.create this.PostId
    and! comment = CommentId.create this.CommentId
    return { ReplyEmailStop.Token = token; Post = post; Comment = comment }
  }

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

/// The writer of a comment, as the comment entity keeps it.
[<RequireQualifiedAccess>]
type internal DbCommentWriterKey =
  | Commenter of commenterId : string
  | Author

module internal DbCommentWriterKey =
  let ofEntityValues (writer : string option) (commenterId : string) =
    match writer, commenterId with
    | (None | Some "commenter"), "" ->
      Validation.error "DbComment.CommenterId is required when the writer is a commenter, but was empty"
    | (None | Some "commenter"), commenterId -> Validation.ok (DbCommentWriterKey.Commenter commenterId)
    | Some "author", "" -> Validation.ok DbCommentWriterKey.Author
    | Some "author", commenterId ->
      Validation.error $"DbComment.CommenterId must be empty when the writer is the author, but was '{commenterId}'"
    | Some other, _ -> Validation.error $"DbComment.Writer must be 'commenter' or 'author', but was '{other}'"

  /// The values of the Writer and CommenterId properties.
  let toEntityValues key =
    match key with
    | DbCommentWriterKey.Commenter commenterId -> "commenter", commenterId
    | DbCommentWriterKey.Author -> "author", ""

  let commenterId key =
    match key with
    | DbCommentWriterKey.Commenter commenterId -> Some commenterId
    | DbCommentWriterKey.Author -> None

/// The writer of a comment, with the record of its commenter.
[<RequireQualifiedAccess>]
type internal DbCommentWriter =
  | Commenter of DbCommenter
  | Author

module internal DbCommentWriter =
  /// None when the commenter of the key is not in the commenters.
  let tryOfKey (commenters : DbCommenter seq) key =
    match key with
    | DbCommentWriterKey.Author -> Some DbCommentWriter.Author
    | DbCommentWriterKey.Commenter commenterId ->
      commenters
      |> Seq.tryFind (fun commenter -> commenter.ValidationId = commenterId)
      |> Option.map DbCommentWriter.Commenter

type internal DbReplyEmailConsent =
  { ConsentDate : DateTime
    StopToken : string
    StopDate : DateTime option }

  static member FromEntity (entity : TableEntity) =
    match "ReplyEmailsConsentDate" |> entity.TryGetString, "ReplyEmailsStopToken" |> entity.TryGetString with
    | None, None -> Validation.ok None
    | Some consentDate, Some stopToken -> validation {
        let! consentDate = consentDate |> DateTime.TryValidateUtc
        and! stopDate = "ReplyEmailsStopDate" |> entity.TryGetString |> Option.traverseValidation DateTime.TryValidateUtc
        return Some { ConsentDate = consentDate; StopToken = stopToken; StopDate = stopDate }
      }
    | _ ->
      Validation.error
        $"DbComment.ReplyEmailsConsentDate and ReplyEmailsStopToken must both be set or both be missing, in TableEntity with RowKey: '{entity.RowKey}'"

  static member OfConsent (consent : ReplyEmailConsent) =
    { ConsentDate = consent.ConsentDate
      StopToken = consent.StopToken.ToString()
      StopDate = consent.StopDate }

  static member WriteTo (entity : TableEntity) (consent : DbReplyEmailConsent option) =
    let utc (value : DateTime) = value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)
    entity["ReplyEmailsConsentDate"] <- consent |> Option.map (_.ConsentDate >> utc) |> Option.toObj
    entity["ReplyEmailsStopToken"] <- consent |> Option.map _.StopToken |> Option.toObj
    entity["ReplyEmailsStopDate"] <- consent |> Option.bind _.StopDate |> Option.map utc |> Option.toObj

  /// Changes only the consent properties of a stored comment entity.
  static member Update (consent : ReplyEmailConsent) (entity : TableEntity) =
    DbReplyEmailConsent.WriteTo entity (Some (DbReplyEmailConsent.OfConsent consent))
    entity

  member this.ToConsent() =
    ReplyEmailStopToken.create this.StopToken
    |> Validation.map (fun stopToken ->
      { ReplyEmailConsent.ConsentDate = this.ConsentDate; StopToken = stopToken; StopDate = this.StopDate })

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
///    "Writer": "&lt;commenter | author&gt;" | null,
///    "ReplyEmailsConsentDate": "&lt;date&gt;" | null,
///    "ReplyEmailsStopToken": "&lt;guid&gt;" | null,
///    "ReplyEmailsStopDate": "&lt;date&gt;" | null
///}
/// </c>
/// A missing <c>ParentId</c> is a top-level comment, and a missing <c>Writer</c> is a commenter.
/// <c>CommenterId</c> is empty when the writer is the author. A missing <c>ReplyEmailsConsentDate</c> is a comment
/// with no reply-email consent.
/// </remarks>
type internal DbComment =
  { Id : string
    PostId : string
    CreatedDate : DateTime
    Content : string
    ParentId : string option
    Writer : DbCommentWriterKey
    ReplyEmails : DbReplyEmailConsent option }
  
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
    let! writer = DbCommentWriterKey.ofEntityValues ("Writer" |> entity.TryGetString) commenterId
    let! replyEmails = DbReplyEmailConsent.FromEntity entity

    return
      { Id = id
        PostId = postId
        CreatedDate = createdDate
        Content = content
        ParentId = parentId
        Writer = writer
        ReplyEmails = replyEmails }
  }
  
  member this.ToEntity() =
    let entity = TableEntity(partitionKey = this.PostId, rowKey = this.Id)
    entity["CreatedDate"] <- this.CreatedDate.ToString("o", CultureInfo.InvariantCulture)
    entity["Content"] <- this.Content
    let writer, commenterId = this.Writer |> DbCommentWriterKey.toEntityValues
    entity["CommenterId"] <- commenterId
    entity["ParentId"] <- this.ParentId |> Option.toObj
    entity["Writer"] <- writer
    DbReplyEmailConsent.WriteTo entity this.ReplyEmails
    entity

  static member OfCommenter
    (
      id : string,
      createdDate : DateTime,
      post : Slug,
      commenter : Commenter,
      content : NonEmptyString,
      topLevel : Comment option,
      replyEmails : ReplyEmailConsent option
    ) =
    { Id = id
      PostId = Slug.value post
      CreatedDate = createdDate
      Content = NonEmptyString.value content
      ParentId = topLevel |> Option.map (_.Id >> string)
      Writer = DbCommentWriterKey.Commenter (commenter.ValidationId.ToString())
      ReplyEmails = replyEmails |> Option.map DbReplyEmailConsent.OfConsent }

  static member OfAuthorReply(id : string, createdDate : DateTime, post : Slug, content : NonEmptyString, topLevel : Comment) =
    { Id = id
      PostId = Slug.value post
      CreatedDate = createdDate
      Content = NonEmptyString.value content
      ParentId = Some (string topLevel.Id)
      Writer = DbCommentWriterKey.Author
      ReplyEmails = None }

  member this.ToComment(status: DbCommentStatus, writer: DbCommentWriter) = validation {
    let! writer =
      match writer with
      | DbCommentWriter.Commenter commenter -> commenter.ToCommenter() |> Validation.map CommentWriter.Commenter
      | DbCommentWriter.Author -> Validation.ok CommentWriter.Author

    let! replyEmails = this.ReplyEmails |> Option.traverseValidation _.ToConsent()

    return!
      Comment.create
        this.Id
        (this.CreatedDate.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture))
        {| Approval = status.Approval
           CreatedDate = status.CreatedDate.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) |}
        this.ParentId
        writer
        replyEmails
        this.Content
  }

  /// A comment whose post is not in the post table gets the slug as its title, so that the author can
  /// still moderate it.
  member this.ToPostedComment(post: DbPost option, status: DbCommentStatus, writer: DbCommentWriter) = validation {
    let! slug = Slug.create this.PostId
    let! title =
      post
      |> Option.map _.Title
      |> Option.defaultValue this.PostId
      |> NonEmptyString.create (Some "title")
    let! comment = this.ToComment(status, writer)
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
      -> CancellableTaskResult<(DbComment * DbCommentWriter * DbCommentStatus) list, DependencyError>
  abstract member GetRepliesOf:
    approvals: string list
      -> topLevelIds: string seq
      -> CancellableTaskResult<(DbComment * DbCommentWriter * DbCommentStatus) list, DependencyError>
  abstract member GetCommentsOnAllPosts:
    approvals: string list
      -> CancellableTaskResult<(DbComment * DbCommentWriter * DbCommentStatus) list, DependencyError>
  abstract member GetCommentsOfCommenter:
    approvals: string list
      -> commenter: DbCommenter
      -> CancellableTaskResult<(DbComment * DbCommentWriter * DbCommentStatus) list, DependencyError>
  abstract member GetCommentById: commentId: string -> CancellableTaskResult<(DbComment * DbCommentWriter * DbCommentStatus) option, DependencyError>
  abstract member CreateComment:
    comment: DbComment
      -> status: DbCommentStatus
      -> CancellableTaskResult<DbComment * DbCommentWriter * DbCommentStatus, DependencyError>
  abstract member UpdateCommentStatus: status: DbCommentStatus -> CancellableTaskResult<DbCommentStatus, DependencyError>
  abstract member DeleteComment: commentId: string -> CancellableTaskResult<unit, DependencyError>
  abstract member CreateReplyEmailStop: stop: DbReplyEmailStop -> CancellableTaskResult<DbReplyEmailStop, DependencyError>
  abstract member GetReplyEmailStop: token: string -> CancellableTaskResult<DbReplyEmailStop option, DependencyError>
  abstract member UpdateReplyEmailConsent:
    postId: string -> commentId: string -> consent: ReplyEmailConsent -> CancellableTaskResult<unit, DependencyError>


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
      match DbCommentWriter.tryOfKey commenters comment.Writer with
      | None ->
        let _, commenterId = DbCommentWriterKey.toEntityValues comment.Writer
        logger.LogWarning(
          "No commenter with Id '{commenterId}' for comment '{commentId}'. The comment is left out.",
          commenterId,
          comment.Id
        )
        return comments
      | Some writer ->
        let! latestStat =
          statuses
          |> Array.filter (fun st -> st.CommentId = comment.Id)
          |> Array.tryHead
          |> Result.requireSome $"No status found for comment with Id '{comment.Id}'"

        if approvals |> List.contains latestStat.Approval
        then return [|(comment, writer, latestStat)|] |> Array.append comments
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
    let replyEmailStopsTable = "replyEmailStops"

    let getCommenterByValidationId validationId =
      validationId |> trySingle commentersTable DbCommenter.FromEntity (Some validationId)

    let writerOf (comment : DbComment) = cancellableTaskResult {
      let! commenter =
        comment.Writer
        |> DbCommentWriterKey.commenterId
        |> Option.traverseCancellableTaskResult getCommenterByValidationId

      return comment.Writer |> DbCommentWriter.tryOfKey (commenter |> Option.flatten |> Option.toList)
    }

    let withWriters approvals (dbComments : DbComment array) = cancellableTaskResult {
      let getCommenters =
        dbComments
        |> Array.choose (_.Writer >> DbCommentWriterKey.commenterId)
        |> whereAnyKey commentersTable DbCommenter.FromEntity (fun commenterId ->
          eq "PartitionKey" commenterId + eq "RowKey" commenterId
        )

      let getCommentStatuses =
        dbComments
        |> Array.map _.Id
        |> whereAnyKey commentStatusesTable DbCommentStatus.FromEntity (eq "PartitionKey")

      let! commenters, statuses = CancellableTaskResult.parallelZip getCommenters getCommentStatuses

      return! withLatestStatus logger approvals dbComments commenters statuses
    }

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
        let! dbComments = eq "PartitionKey" post.Slug |> where commentsTable DbComment.FromEntity
        return! withWriters approvals (dbComments |> List.toArray)
      }

      member _.GetRepliesOf approvals topLevelIds = cancellableTaskResult {
        let! dbComments = topLevelIds |> whereAnyKey commentsTable DbComment.FromEntity (eq "ParentId")
        return! withWriters approvals (dbComments |> List.toArray)
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
        let! dbComment = commentId |> trySingle commentsTable DbComment.FromEntity None

        match dbComment with
        | None -> return None
        | Some dbComment ->
          let! dbWriter = writerOf dbComment
          let! statuses = eq "PartitionKey" commentId |> where commentStatusesTable DbCommentStatus.FromEntity

          let! dbCommentStatus =
            statuses
            |> List.filter (fun status -> status.CommentId = commentId)
            |> List.sortByDescending _.CreatedDate
            |> List.tryHead
            |> Result.requireSome (
              PostDbError.validation $"No status found for comment with Id '{commentId}'"
            )

          return dbWriter |> Option.map (fun writer -> dbComment, writer, dbCommentStatus)
      }
      
      member _.CreateComment comment status = cancellableTaskResult {
        let! dbComment = comment.ToEntity() |> add commentsTable PostDbError.validation DbComment.FromEntity
        let! dbCommentStatus = status.ToEntity() |> add commentStatusesTable PostDbError.validation DbCommentStatus.FromEntity
        let! dbWriter = writerOf dbComment
        let! dbWriter = dbWriter |> Result.requireSome (
          PostDbError.validation $"Failed to fetch commenter for newly created comment with Id '{dbComment.Id}'"
        )
        return dbComment, dbWriter, dbCommentStatus
      }
      
      member _.UpdateCommentStatus status =
        status.ToEntity() |> add commentStatusesTable PostDbError.validation DbCommentStatus.FromEntity
      
      member _.DeleteComment commentId = cancellableTaskResult {
        do! eq "PartitionKey" commentId |> delete commentStatusesTable PostDbError.validation
        do! eq "RowKey" commentId |> delete commentsTable PostDbError.validation
      }

      member _.CreateReplyEmailStop stop =
        stop.ToEntity() |> add replyEmailStopsTable PostDbError.validation DbReplyEmailStop.FromEntity

      member _.GetReplyEmailStop token =
        token |> trySingle replyEmailStopsTable DbReplyEmailStop.FromEntity (Some token)

      member _.UpdateReplyEmailConsent postId commentId consent =
        TableEntity(postId, commentId)
        |> update commentsTable PostDbError.validation DbComment.FromEntity
        <| DbReplyEmailConsent.Update consent
        |> CancellableTaskResult.ignore
    }
  
