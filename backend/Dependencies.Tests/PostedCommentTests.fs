module TwoPoint.Dependencies.Tests.PostedCommentTests

open TwoPoint.Core.Shared
open TwoPoint.Dependencies.Posts

open Expecto

open System

let private date = DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)

let private comment =
  { DbComment.Id = "8f6c1b2e-0d4a-4c3e-9b1a-2f5d6e7a8b9c"
    PostId = "first-post"
    CreatedDate = date
    Content = "A comment"
    CommenterId = "ada@example.com" }

let private commenter =
  { DbCommenter.ValidationId = "1a2b3c4d-5e6f-4a1b-8c2d-3e4f5a6b7c8d"
    EmailAddress = "ada@example.com"
    Name = Some "Ada"
    CreatedDate = date
    Status = "new" }

let private status =
  { DbCommentStatus.Id = "2b3c4d5e-6f7a-4b2c-9d3e-4f5a6b7c8d9e"
    CommentId = comment.Id
    CreatedDate = date
    Approval = "new" }

let private title (post : DbPost option) =
  comment.ToPostedComment(post, status, commenter)
  |> Result.map (_.PostTitle >> NonEmptyString.value)

[<Tests>]
let tests =
  testList "PostedComment" [
    test "a posted comment has the title of its post" {
      let post = { DbPost.Title = "First post"; Slug = "first-post"; CreatedDate = date }
      Expect.equal (title (Some post)) (Ok "First post") "Expected the post title"
    }

    test "a comment whose post is not in the post table has the slug as its title" {
      Expect.equal (title None) (Ok "first-post") "Expected the slug"
    }
  ]
