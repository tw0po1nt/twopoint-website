module TwoPoint.Dependencies.Tests.CommentEntityTests

open TwoPoint.Core.Posts
open TwoPoint.Core.Shared
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

let private read (writer : DbCommentWriter) (entity : TableEntity) =
  DbComment.FromEntity entity |> Result.bind _.ToComment(status, writer)

let private parent (comment : Comment) = comment.Parent |> Option.map string

let private commenterId (comment : Comment) =
  comment |> Comment.commenter |> Option.map (_.ValidationId >> string)

[<Tests>]
let tests =
  testList "Comment entity" [
    test "an entity with no ParentId and no Writer reads as a top-level comment from its commenter" {
      let entity = entityWith [ "CommenterId", commenter.ValidationId ]

      Expect.equal
        (entity |> DbComment.FromEntity |> Result.map _.Writer)
        (Ok (DbCommentWriterKey.Commenter commenter.ValidationId))
        "Expected the key of the commenter"

      match entity |> read (DbCommentWriter.Commenter commenter) with
      | Ok comment ->
        Expect.equal (parent comment) None "Expected a top-level comment"
        Expect.equal (commenterId comment) (Some commenter.ValidationId) "Expected the commenter"
      | Error errors -> failtestf "Expected a comment, got %A" errors
    }

    test "a reply from a commenter reads back with its parent and its commenter" {
      let entity =
        entityWith [ "CommenterId", commenter.ValidationId; "ParentId", parentId; "Writer", "commenter" ]

      match entity |> read (DbCommentWriter.Commenter commenter) with
      | Ok comment ->
        Expect.equal (parent comment) (Some parentId) "Expected the parent"
        Expect.equal (commenterId comment) (Some commenter.ValidationId) "Expected the commenter"
      | Error errors -> failtestf "Expected a comment, got %A" errors
    }

    test "a reply from the author reads back with its parent and the author" {
      let entity = entityWith [ "CommenterId", ""; "ParentId", parentId; "Writer", "author" ]

      match entity |> read DbCommentWriter.Author with
      | Ok comment ->
        Expect.equal (parent comment) (Some parentId) "Expected the parent"
        Expect.equal comment.Writer CommentWriter.Author "Expected the author"
      | Error errors -> failtestf "Expected a comment, got %A" errors
    }

    test "a new reply from a commenter is stored with the ID of its top-level comment" {
      let topLevel = entityWith [ "CommenterId", commenter.ValidationId ] |> read (DbCommentWriter.Commenter commenter)
      let replier = commenter.ToCommenter()

      match topLevel, replier with
      | Ok topLevel, Ok replier ->
        let reply =
          DbComment.OfCommenter(
            parentId,
            date,
            Slug.Unsafe.create "first-post",
            replier,
            NonEmptyString.Unsafe.create None "A reply",
            Some topLevel
          )

        match reply.ToEntity() |> read (DbCommentWriter.Commenter commenter) with
        | Ok comment -> Expect.equal (parent comment) (Some commentId) "Expected the top-level comment as the parent"
        | Error errors -> failtestf "Expected a comment, got %A" errors
      | topLevel, replier -> failtestf "Expected a comment and a commenter, got %A and %A" topLevel replier
    }

    test "an entity with an unknown Writer is refused" {
      let entity = entityWith [ "CommenterId", commenter.ValidationId; "Writer", "editor" ]

      Expect.isError (DbComment.FromEntity entity) "Expected a refusal"
    }

    test "an entity from the author with a CommenterId is refused" {
      let entity = entityWith [ "CommenterId", commenter.ValidationId; "Writer", "author" ]

      Expect.isError (DbComment.FromEntity entity) "Expected a refusal"
    }

    test "an entity from a commenter with an empty CommenterId is refused" {
      for properties in [ [ "CommenterId", "" ]; [ "CommenterId", ""; "Writer", "commenter" ] ] do
        Expect.isError (entityWith properties |> DbComment.FromEntity) "Expected a refusal"
    }

    test "a comment written to an entity reads back with the same values" {
      let comment =
        { DbComment.Id = commentId
          PostId = "first-post"
          CreatedDate = date
          Content = "A reply"
          ParentId = Some parentId
          Writer = DbCommentWriterKey.Author }

      let entity = comment.ToEntity()

      Expect.equal (entity.GetString "CommenterId") "" "Expected an empty CommenterId"
      Expect.equal (DbComment.FromEntity entity) (Ok comment) "Expected the same comment"
    }

    test "a top-level comment written to an entity reads back with no parent" {
      let comment =
        { DbComment.Id = commentId
          PostId = "first-post"
          CreatedDate = date
          Content = "A comment"
          ParentId = None
          Writer = DbCommentWriterKey.Commenter commenter.ValidationId }

      Expect.equal (comment.ToEntity() |> DbComment.FromEntity) (Ok comment) "Expected the same comment"
    }
  ]
