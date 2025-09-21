namespace TwoPoint.Core.Posts.Dependencies

open TwoPoint.Core.Posts
open TwoPoint.Core.Shared
open TwoPoint.Core.Shared.Impl
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
///    "PartitionKey": "&lt;email&gt;",
///    "RowKey": "&lt;email&gt;",
///    "Timestamp": "&lt;date&gt;",
///    "CreatedDate": "&lt;date&gt;",
///    "Name": "&lt;string&gt;" | null,
///    "Status": "&lt;new | trusted | banned&gt;"
///  }
/// </c>
/// </remarks>
type internal DbCommenter =
  { EmailAddress : string
    Name : string option
    CreatedDate : DateTime
    Status : string }
  
  static member FromEntity (entity : TableEntity) = validation {
    let entityName = nameof DbCommenter
    let! createdDate =
      "CreatedDate"
      |> entity.RequireString entityName
      |> Validation.bind DateTime.TryValidateUtc
    let name = "Name" |> entity.TryGetString
    let! status = "Status" |> entity.RequireString entityName

    return
      { EmailAddress = entity.RowKey
        Name = name
        CreatedDate = createdDate
        Status = status }
  }
  
  member this.ToEntity() =
    let entity = TableEntity(partitionKey = this.EmailAddress, rowKey = this.EmailAddress)
    entity["CreatedDate"] <- this.CreatedDate.ToString("o", CultureInfo.InvariantCulture)
    entity["Name"] <- this.Name |> Option.toObj
    entity["Status"] <- this.Status
    entity
    
  member this.ToCommenter() =
    Commenter.create
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
///    "CommenterId": "&lt;commenter rowKey&gt;"
///}
/// </c>
/// </remarks>
type internal DbComment =
  { Id : string
    PostId : string
    CreatedDate : DateTime
    Content : string
    CommenterId : string }
  
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

    return
      { Id = id
        PostId = postId
        CreatedDate = createdDate
        Content = content
        CommenterId = commenterId } 
  }
  
  member this.ToEntity() =
    let entity = TableEntity(partitionKey = this.PostId, rowKey = this.Id)
    entity["CreatedDate"] <- this.CreatedDate.ToString("o", CultureInfo.InvariantCulture)
    entity["Content"] <- this.Content
    entity["CommenterId"] <- this.CommenterId
    entity
  
  member this.ToComment(status: DbCommentStatus, commenter: DbCommenter) =
    Comment.create
      this.Id
      (this.CreatedDate.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture))
      {| Approval = status.Approval
         CreatedDate = status.CreatedDate.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) |}
      {| CreatedDate = commenter.CreatedDate.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)
         EmailAddress = commenter.EmailAddress
         Name = commenter.Name
         Status = commenter.Status |}
      this.Content
      

type internal IPostDb =
  abstract member GetAllPosts: unit -> CancellableTaskResult<DbPost list, DependencyError>
  abstract member GetPostBySlug: slug: string -> CancellableTaskResult<DbPost option, DependencyError>
  abstract member CreatePost: post: DbPost -> CancellableTaskResult<DbPost, DependencyError>
  
  abstract member GetCommenterByEmailAddress: emailAddress: string -> CancellableTaskResult<DbCommenter option, DependencyError>
  abstract member CreateCommenter: commenter: DbCommenter -> CancellableTaskResult<DbCommenter, DependencyError>
  abstract member UpdateCommenterStatus:
    commenter: DbCommenter
      -> status: string
      -> CancellableTaskResult<DbCommenter, DependencyError>
  
  abstract member GetCommentsForPost: post: DbPost -> CancellableTaskResult<(DbComment * DbCommenter * DbCommentStatus) list, DependencyError>
  abstract member GetCommentById: commentId: string -> CancellableTaskResult<(DbComment * DbCommenter * DbCommentStatus) option, DependencyError>
  abstract member CreateComment:
    comment: DbComment
      -> status: DbCommentStatus
      -> CancellableTaskResult<DbComment * DbCommenter * DbCommentStatus, DependencyError>
  abstract member UpdateCommentStatus: status: DbCommentStatus -> CancellableTaskResult<DbCommentStatus, DependencyError>


module internal PostDbError =
  let private postDbError =
    { Dependency = Dependency.Database
      Type = Unknown (exn "") }
    
  let validation msg = { postDbError with Type = DependencyErrorType.Validation msg }
    
  let unknown msg = { postDbError with Type = msg |> FSharp.Core.exn |> DependencyErrorType.Unknown }


module internal PostDb =
  
  open Azure.Data.Tables.FSharp
  open Microsoft.Extensions.Logging
  
  let live (tableServiceClient : TableServiceClient) (logger : ILogger) =

    let db = nameof IPostDb

    let trySingle table onEntity partition key =
      Db.trySingle tableServiceClient logger db table onEntity partition key
      
    let toList table onEntity =
      Db.toList tableServiceClient logger db table onEntity
      
    let where table onEntity filter =
      Db.where tableServiceClient logger db table onEntity filter
      
    let add table onError onEntity entity =
      Db.add tableServiceClient logger db table onError onEntity entity
      
    let update table onError onEntity entity updateEntity =
      Db.update tableServiceClient logger db table onError onEntity entity updateEntity
      
    let postsTable = "posts"
    let commentsTable = "comments"
    let commentStatusesTable = "commentStatuses"
    let commentersTable = "commenters"
    
    { new IPostDb with
      
      member this.GetAllPosts () =
        toList postsTable DbPost.FromEntity
      
      member this.GetPostBySlug slug =
        slug |> trySingle postsTable DbPost.FromEntity (Some slug)

      member this.CreatePost post =
        post.ToEntity() |> add postsTable PostDbError.validation DbPost.FromEntity

      member this.GetCommenterByEmailAddress emailAddress =
        emailAddress |> trySingle commentersTable DbCommenter.FromEntity (Some emailAddress)

      member this.CreateCommenter commenter =
        commenter.ToEntity() |> add commentersTable PostDbError.validation DbCommenter.FromEntity
        
      member this.UpdateCommenterStatus commenter status =
        commenter.ToEntity() |> update commentersTable PostDbError.validation DbCommenter.FromEntity
        <| fun entity ->
          entity["Status"] <- status
          entity
        
      member this.GetCommentsForPost post = cancellableTaskResult {
        // Get comments
        let! dbComments = (eq "PartitionKey" post.Slug) |> where commentsTable DbComment.FromEntity
        let dbComments = dbComments |> List.toArray
      
        // for every comment, get the commenter
        let commenterIds = dbComments |> Array.map _.CommenterId
        let commentersFilter = (Filter.Empty, commenterIds) ||> Array.fold (fun filter commenterId ->
          filter * (eq "PartitionKey" commenterId + eq "RowKey" commenterId)
        )
        let getCommenters = commentersFilter |> where commentersTable DbCommenter.FromEntity
        
        // for every comment, get the comment statuses
        let commentIds = dbComments |> Array.map _.Id
        let commentStatusesFilter = (Filter.Empty, commentIds) ||> Array.fold (fun filter commentId ->
          filter * (eq "PartitionKey" commentId)
        )
        let getCommentStatuses = commentStatusesFilter |> where commentStatusesTable DbCommentStatus.FromEntity
        
        let! commenters, statuses = CancellableTaskResult.parallelZip getCommenters getCommentStatuses
        
        let commenters = commenters |> List.toArray
        let statuses =
          statuses
          |> List.toArray
          |> Array.sortByDescending (fun (status: DbCommentStatus) -> status.CreatedDate)
        
        return!
          (Validation.ok [||], dbComments) ||> Array.fold (fun comments comment -> validation {
            let! comments = comments
            let! commenter =
              commenters
              |> Array.tryFind (fun ctr -> ctr.EmailAddress = comment.CommenterId)
              |> Validation.requireSome $"No commenter with Id '{comment.CommenterId}' found for comment with Id '{comment.Id}'"
            let! latestStat =
              statuses
              |> Array.filter (fun st -> st.CommentId = comment.Id)
              |> Array.tryHead
              |> Result.requireSome $"No status found for comment with Id '{comment.Id}'"            
            return [|(comment, commenter, latestStat)|] |> Array.append comments
          })
          |> DependencyError.ofValidation Dependency.Database
          |> Result.map Array.toList
      }
      
      member this.GetCommentById commentId = cancellableTaskResult {
        // Get comment
        let! dbComment = commentId |> trySingle commentsTable DbComment.FromEntity None
        let! dbCommenter =
          dbComment
          |> Option.traverseCancellableTaskResult (fun dbComment -> this.GetCommenterByEmailAddress dbComment.CommenterId)
          |> CancellableTaskResult.map Option.flatten

        let! statuses =
          dbComment
          |> Option.traverseCancellableTaskResult (fun dbComment ->
            (eq "PartitionKey" commentId) |> where commentStatusesTable DbCommentStatus.FromEntity
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
          return (comment, commenter, dbCommentStatus)
        }
      }
      
      member this.CreateComment comment status = cancellableTaskResult {
        let! dbComment = comment.ToEntity() |> add commentsTable PostDbError.validation DbComment.FromEntity
        let! dbCommentStatus = status.ToEntity() |> add commentStatusesTable PostDbError.validation DbCommentStatus.FromEntity
        let! (dbCommenter: DbCommenter option) = this.GetCommenterByEmailAddress comment.CommenterId
        let! dbCommenter = dbCommenter |> Result.requireSome (
          PostDbError.validation $"Failed to fetch commenter for newly created comment with Id '{dbComment.Id}'"
        )
        return (dbComment, dbCommenter, dbCommentStatus)
      }
      
      member this.UpdateCommentStatus status =
        status.ToEntity() |> add commentStatusesTable PostDbError.validation DbCommentStatus.FromEntity
    }
  
