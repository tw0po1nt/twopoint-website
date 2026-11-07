module TwoPoint.Dependencies.Tests.CommentEntityTests

open TwoPoint.Core.Posts
open TwoPoint.Dependencies.Posts

open Azure.Data.Tables
open Expecto

open System

let private date = DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)

let private commentId = "8f6c1b2e-0d4a-4c3e-9b1a-2f5d6e7a8b9c"
let private parentId = "3c4d5e6f-7a8b-4c3d-8e4f-5a6b7c8d9e0f"

let private commenter =
  { DbCommenter.ValidationId = "1a2b3c4d-5e6f-4a1b-8c2d-3e4f5a6b7c8d"
    EmailAddress = "ada@example.com"
    Name = Some "Ada"
    CreatedDate = date
    Status = "new" }

let private status =
  { DbCommentStatus.Id = "2b3c4d5e-6f7a-4b2c-9d3e-4f5a6b7c8d9e"
    CommentId = commentId
    CreatedDate = date
    Approval = "approved" }

let private entityWith (properties : (string * string) list) =
  let entity = TableEntity("first-post", commentId)
  entity["CreatedDate"] <- date.ToString("o")
  entity["Content"] <- "A comment"

  for key, value in properties do
    entity[key] <- value

  entity

let private read (commenter : DbCommenter option) (entity : TableEntity) =
  DbComment.FromEntity entity |> Result.bind _.ToComment(status, commenter)

let private parent (comment : Comment) = comment.Parent |> Option.map string

let private commenterId (comment : Comment) =
  comment |> Comment.commenter |> Option.map (_.ValidationId >> string)

[<Tests>]
let tests =
  testList "Comment entity" [
    test "an entity with no ParentId and no Writer reads as a top-level comment from its commenter" {
      match entityWith [ "CommenterId", commenter.ValidationId ] |> read (Some commenter) with
      | Ok comment ->
        Expect.equal (parent comment) None "Expected a top-level comment"
        Expect.equal (commenterId comment) (Some commenter.ValidationId) "Expected the commenter"
      | Error errors -> failtestf "Expected a comment, got %A" errors
    }

    test "a reply from a commenter reads back with its parent and its commenter" {
      let entity =
        entityWith [ "CommenterId", commenter.ValidationId; "ParentId", parentId; "Writer", "commenter" ]

      match entity |> read (Some commenter) with
      | Ok comment ->
        Expect.equal (parent comment) (Some parentId) "Expected the parent"
        Expect.equal (commenterId comment) (Some commenter.ValidationId) "Expected the commenter"
      | Error errors -> failtestf "Expected a comment, got %A" errors
    }

    test "a reply from the author reads back with its parent and the author" {
      let entity = entityWith [ "CommenterId", ""; "ParentId", parentId; "Writer", "author" ]

      match entity |> read None with
      | Ok comment ->
        Expect.equal (parent comment) (Some parentId) "Expected the parent"
        Expect.equal comment.Writer CommentWriter.Author "Expected the author"
      | Error errors -> failtestf "Expected a comment, got %A" errors
    }

    test "an entity with an unknown Writer is refused" {
      let entity = entityWith [ "CommenterId", commenter.ValidationId; "Writer", "editor" ]

      Expect.isError (DbComment.FromEntity entity) "Expected a refusal"
    }

    test "a comment written to an entity reads back with the same values" {
      let comment =
        { DbComment.Id = commentId
          PostId = "first-post"
          CreatedDate = date
          Content = "A reply"
          CommenterId = ""
          ParentId = Some parentId
          Writer = DbCommentWriter.Author }

      Expect.equal (comment.ToEntity() |> DbComment.FromEntity) (Ok comment) "Expected the same comment"
    }

    test "a top-level comment written to an entity reads back with no parent" {
      let comment =
        { DbComment.Id = commentId
          PostId = "first-post"
          CreatedDate = date
          Content = "A comment"
          CommenterId = commenter.ValidationId
          ParentId = None
          Writer = DbCommentWriter.Commenter }

      Expect.equal (comment.ToEntity() |> DbComment.FromEntity) (Ok comment) "Expected the same comment"
    }
  ]
