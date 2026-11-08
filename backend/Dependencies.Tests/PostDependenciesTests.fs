module TwoPoint.Dependencies.Tests.PostDependenciesTests

open TwoPoint.Core.Posts
open TwoPoint.Core.Posts.Actions
open TwoPoint.Core.Shared
open TwoPoint.Dependencies.Comms
open TwoPoint.Dependencies.Posts
open TwoPoint.Dependencies.Tests.Fakes

open Expecto
open FsToolkit.ErrorHandling
open Microsoft.Extensions.Logging.Abstractions

open System
open System.Threading

let private now = DateTime(2026, 11, 8, 9, 30, 0, DateTimeKind.Utc)
let private earlier = DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc)

let private commentGuid = Guid "4f8a2c1e-6b3d-4e5f-9a7b-1c2d3e4f5a6b"
let private statusGuid = Guid "7d6c5b4a-3f2e-4d1c-8b0a-9f8e7d6c5b4a"

let private ok result = result |> Result.defaultWith (fun error -> failwithf "%A" error)

let private dbCommenter =
  { DbCommenter.ValidationId = "1a2b3c4d-5e6f-4a1b-8c2d-3e4f5a6b7c8d"
    EmailAddress = "ada@example.com"
    Name = Some "Ada"
    CreatedDate = earlier
    Status = "trusted" }

let private commenter = dbCommenter.ToCommenter() |> ok

let private post =
  let row = { DbPost.Title = "First post"; Slug = "first-post"; CreatedDate = earlier }
  row.ToPost PostCommentStats.Zero |> ok

let private topLevel =
  let id = "3c4d5e6f-7a8b-4c3d-8e4f-5a6b7c8d9e0f"
  let row =
    { DbComment.Id = id
      PostId = "first-post"
      CreatedDate = earlier
      Content = "A comment"
      ParentId = None
      Writer = DbCommentWriterKey.Commenter dbCommenter.ValidationId
      ReplyEmails = None }
  let status = { DbCommentStatus.Id = "9e8d7c6b-5a4f-4e3d-8c2b-1a0f9e8d7c6b"; CommentId = id; CreatedDate = earlier; Approval = "approved" }
  row.ToComment(status, DbCommentWriter.Commenter dbCommenter) |> ok

type private RecordingPostDb() =
  let comments = ResizeArray<DbComment * DbCommentStatus>()

  member _.Comments = List.ofSeq comments

  interface IPostDb with
    member _.GetAllPosts () = notUsed ()
    member _.GetPostBySlug _ = notUsed ()
    member _.UpsertPost _ = notUsed ()
    member _.DeletePost _ = notUsed ()
    member _.HasCommentsOfPost _ = notUsed ()
    member _.DeleteCommentsOfPost _ = notUsed ()
    member _.GetAllCommenters () = notUsed ()
    member _.GetCommenterByEmailAddress _ = notUsed ()
    member _.GetCommenterByValidationId _ = notUsed ()
    member _.CreateCommenter _ = notUsed ()
    member _.UpdateCommenterStatus _ _ = notUsed ()
    member _.DeleteCommenter _ = notUsed ()
    member _.GetCommentsForPost _ _ = notUsed ()
    member _.GetRepliesOf _ _ = notUsed ()
    member _.GetCommentsOnAllPosts _ = notUsed ()
    member _.GetCommentsOfCommenter _ _ = notUsed ()
    member _.GetComment _ _ = notUsed ()

    member _.CreateComment comment status =
      comments.Add((comment, status))
      let writer = DbCommentWriter.tryOfKey [ dbCommenter ] comment.Writer |> Option.get
      CancellableTaskResult.singleton (comment, writer, status)

    member _.UpdateCommentStatus _ = notUsed ()
    member _.DeleteComment _ _ = notUsed ()
    member _.CreateReplyEmailStop _ = notUsed ()
    member _.GetReplyEmailStop _ = notUsed ()
    member _.UpdateReplyEmailConsent _ _ _ = notUsed ()
    member _.GetReplyEmailSentDate _ _ = notUsed ()
    member _.RecordReplyEmailSent _ _ _ = notUsed ()

let private comms = { new ICommsService with member _.SendEmail _ = notUsed () }

let private dependencies (postDb : RecordingPostDb) (ids : Guid list) =
  PostDependencies([], postDb, comms, FixedSystem(ids, now), NullLogger.Instance)

let private onlyRow (postDb : RecordingPostDb) =
  match postDb.Comments with
  | [ row ] -> row
  | rows -> failtestf "Expected one comment row, got %A" rows

[<Tests>]
let tests =
  testList "PostDependencies" [
    test "CreateComment writes the time of the clock on the comment row and on its status row, with a new status ID" {
      let postDb = RecordingPostDb()
      let newComment : NewComment =
        { Post = post.Id
          ValidationId = commenter.ValidationId
          Content = NonEmptyString.Unsafe.create None "A new comment"
          Target = NewCommentTarget.TopLevel ReplyEmailsChoice.NotAsked }

      let created =
        ((dependencies postDb [ statusGuid ]).CreateComment
          (CommentId.ofGuid commentGuid) post commenter newComment None CommentApproval.Approved None
          CancellationToken.None).Result

      Expect.isOk created "Expected the comment"
      let comment, status = onlyRow postDb
      Expect.equal comment.Id (string commentGuid) "Expected the ID that the caller gave"
      Expect.equal comment.CreatedDate now "Expected the time of the clock on the comment row"
      Expect.equal status.Id (string statusGuid) "Expected the ID of the system on the status row"
      Expect.equal status.CreatedDate now "Expected the time of the clock on the status row"
    }

    test "CreateAuthorReply writes a new comment ID, a new status ID, and the time of the clock" {
      let postDb = RecordingPostDb()

      let created =
        ((dependencies postDb [ commentGuid; statusGuid ]).CreateAuthorReply
          post.Id topLevel (NonEmptyString.Unsafe.create None "A reply") CommentApproval.Approved
          CancellationToken.None).Result

      Expect.isOk created "Expected the reply"
      let comment, status = onlyRow postDb
      Expect.equal comment.Id (string commentGuid) "Expected the first ID of the system as the comment ID"
      Expect.equal comment.CreatedDate now "Expected the time of the clock on the comment row"
      Expect.equal comment.ParentId (Some (string topLevel.Id)) "Expected the top-level comment as the parent"
      Expect.equal status.Id (string statusGuid) "Expected the second ID of the system as the status ID"
      Expect.equal status.CommentId (string commentGuid) "Expected the status row of the reply"
      Expect.equal status.CreatedDate now "Expected the time of the clock on the status row"
    }
  ]
