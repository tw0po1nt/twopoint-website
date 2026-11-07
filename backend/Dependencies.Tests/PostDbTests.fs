module TwoPoint.Dependencies.Tests.PostDbTests

open TwoPoint.Dependencies.Posts

open Expecto
open Microsoft.Extensions.Logging.Abstractions

open System

let private date = DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)

let private commenter =
  { DbCommenter.ValidationId = "1a2b3c4d-5e6f-4a1b-8c2d-3e4f5a6b7c8d"
    EmailAddress = "ada@example.com"
    Name = Some "Ada"
    CreatedDate = date
    Status = "new" }

let private comment =
  { DbComment.Id = "8f6c1b2e-0d4a-4c3e-9b1a-2f5d6e7a8b9c"
    PostId = "first-post"
    CreatedDate = date
    Content = "A comment"
    CommenterId = commenter.ValidationId
    ParentId = None
    Writer = DbCommentWriter.Commenter }

let private orphan =
  { comment with
      Id = "9a7d2c3f-1e5b-4d4f-8c2b-3a6e7f8b9c0d"
      CommenterId = "2b3c4d5e-6f7a-4b2c-9d3e-4f5a6b7c8d9e" }

let private statusOf (comment : DbComment) =
  { DbCommentStatus.Id = $"status-{comment.Id}"
    CommentId = comment.Id
    CreatedDate = date
    Approval = "new" }

[<Tests>]
let tests =
  testList "PostDb.withLatestStatus" [
    test "a comment with no commenter is left out, and the other comments stay" {
      let rows =
        PostDb.withLatestStatus NullLogger.Instance [ "new" ] [| comment; orphan |] [ commenter ] [ statusOf comment; statusOf orphan ]

      Expect.equal rows (Ok [ comment, Some commenter, statusOf comment ]) "Expected only the comment with a commenter"
    }

    test "a comment of the author stays with no commenter" {
      let reply =
        { comment with
            Id = "4d5e6f7a-8b9c-4d4e-9f5a-6b7c8d9e0f1a"
            CreatedDate = date.AddMinutes 1.0
            CommenterId = ""
            ParentId = Some comment.Id
            Writer = DbCommentWriter.Author }

      let rows =
        PostDb.withLatestStatus NullLogger.Instance [ "new" ] [| comment; reply |] [ commenter ] [ statusOf comment; statusOf reply ]

      Expect.equal
        rows
        (Ok [ comment, Some commenter, statusOf comment; reply, None, statusOf reply ])
        "Expected the comment and the reply of the author"
    }
  ]
