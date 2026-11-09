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
let private stopToken = "6b5a4f3e-2d1c-4b0a-9f8e-7d6c5b4a3f2e"

let private commenter =
  { DbCommenter.ValidationId = "1a2b3c4d-5e6f-4a1b-8c2d-3e4f5a6b7c8d"
    EmailAddress = "ada@example.com"
    Name = Some "Ada"
    CreatedDate = date
    Status = "new"
    ValidationEmailSentDate = None }

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

let private consentOn (consentDate : DateTime) =
  { ReplyEmailConsent.ConsentDate = consentDate
    StopToken = ReplyEmailStopToken.ofGuid (Guid stopToken)
    StopDate = None }

let private storedWith (consent : ReplyEmailConsent) =
  match commenter.ToCommenter() with
  | Ok writer ->
    DbComment
      .OfCommenter(
        commentId,
        date,
        Slug.Unsafe.create "first-post",
        writer,
        NonEmptyString.Unsafe.create None "A comment",
        None,
        Some consent
      )
      .ToEntity()
  | Error errors -> failwithf "Expected a commenter, got %A" errors

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

    test "an entity with no consent properties reads as a comment with no consent" {
      match entityWith [ "CommenterId", commenter.ValidationId ] |> read (DbCommentWriter.Commenter commenter) with
      | Ok comment -> Expect.isNone comment.ReplyEmails "Expected no consent"
      | Error errors -> failtestf "Expected a comment, got %A" errors
    }

    test "an entity with a consent reads back with the consent date, the stop token, and the stop date" {
      let stopDate = date.AddDays 3.0

      let entity =
        entityWith
          [ "CommenterId", commenter.ValidationId
            "ReplyEmailsConsentDate", date.ToString("o")
            "ReplyEmailsStopToken", stopToken
            "ReplyEmailsStopDate", stopDate.ToString("o") ]

      match entity |> read (DbCommentWriter.Commenter commenter) with
      | Ok { ReplyEmails = Some consent } ->
        Expect.equal consent.ConsentDate date "Expected the consent date"
        Expect.equal (string consent.StopToken) stopToken "Expected the stop token"
        Expect.equal consent.StopDate (Some stopDate) "Expected the stop date"
      | other -> failtestf "Expected a comment with a consent, got %A" other
    }

    test "an entity with a consent date and no stop token is refused" {
      let entity = entityWith [ "CommenterId", commenter.ValidationId; "ReplyEmailsConsentDate", date.ToString("o") ]

      Expect.isError (DbComment.FromEntity entity) "Expected a refusal"
    }

    test "a new comment with a consent is stored with the consent and no stop date" {
      let consent = consentOn date
      let entity = storedWith consent
      Expect.isFalse (entity.ContainsKey "ReplyEmailsStopDate" && not (isNull entity["ReplyEmailsStopDate"])) "Expected no stop date"

      match entity |> read (DbCommentWriter.Commenter commenter) with
      | Ok read -> Expect.equal read.ReplyEmails (Some consent) "Expected the same consent"
      | Error errors -> failtestf "Expected a comment, got %A" errors
    }

    test "a stop written to a stored comment adds the stop date and keeps the other properties" {
      let consent = consentOn date
      let stopped = { consent with StopDate = Some (date.AddDays 3.0) }
      let entity = storedWith consent |> DbReplyEmailConsent.Update stopped

      match entity |> read (DbCommentWriter.Commenter commenter) with
      | Ok read ->
        Expect.equal read.ReplyEmails (Some stopped) "Expected the consent with the stop date"
        Expect.equal (NonEmptyString.value read.Content) "A comment" "Expected the same content"
        Expect.equal (commenterId read) (Some commenter.ValidationId) "Expected the same commenter"
      | Error errors -> failtestf "Expected a comment, got %A" errors
    }

    test "a stop row reads back as the stop of its token, post, and comment" {
      let stop =
        { ReplyEmailStop.Token = ReplyEmailStopToken.ofGuid (Guid stopToken)
          Post = Slug.Unsafe.create "first-post"
          Comment = CommentId.ofGuid (Guid commentId) }

      Expect.equal
        (DbReplyEmailStop.OfStop(stop).ToEntity() |> DbReplyEmailStop.FromEntity |> Result.bind _.ToStop())
        (Ok stop)
        "Expected the same stop"
    }

    test "a consent date in local time is stored in UTC and reads back as the same instant" {
      let entity = storedWith (consentOn (date.ToLocalTime()))

      match entity |> read (DbCommentWriter.Commenter commenter) with
      | Ok { ReplyEmails = Some consent } -> Expect.equal consent.ConsentDate date "Expected the same instant in UTC"
      | other -> failtestf "Expected a comment with a consent, got %A" other
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
            Some topLevel,
            None
          )

        match reply.ToEntity() |> read (DbCommentWriter.Commenter commenter) with
        | Ok comment -> Expect.equal (parent comment) (Some commentId) "Expected the top-level comment as the parent"
        | Error errors -> failtestf "Expected a comment, got %A" errors
      | topLevel, replier -> failtestf "Expected a comment and a commenter, got %A and %A" topLevel replier
    }

    test "a new reply from the author is stored with the ID of its top-level comment and no commenter" {
      match entityWith [ "CommenterId", commenter.ValidationId ] |> read (DbCommentWriter.Commenter commenter) with
      | Ok topLevel ->
        let reply =
          DbComment.OfAuthorReply(
            parentId,
            date,
            Slug.Unsafe.create "first-post",
            NonEmptyString.Unsafe.create None "An answer",
            topLevel
          )

        let entity = reply.ToEntity()
        Expect.equal (entity.GetString "CommenterId") "" "Expected an empty CommenterId"

        match entity |> read DbCommentWriter.Author with
        | Ok comment ->
          Expect.equal (parent comment) (Some commentId) "Expected the top-level comment as the parent"
          Expect.equal comment.Writer CommentWriter.Author "Expected the author"
        | Error errors -> failtestf "Expected a comment, got %A" errors
      | Error errors -> failtestf "Expected a comment, got %A" errors
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
          Writer = DbCommentWriterKey.Author
          ReplyEmails = None }

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
          Writer = DbCommentWriterKey.Commenter commenter.ValidationId
          ReplyEmails = None }

      Expect.equal (comment.ToEntity() |> DbComment.FromEntity) (Ok comment) "Expected the same comment"
    }
  ]

[<Tests>]
let commenterTests =
  testList "DbCommenter" [
    test "a commenter with the date of a validation email reads back with the same date" {
      let sent = { commenter with ValidationEmailSentDate = Some (date.AddHours 2.0) }

      Expect.equal (sent.ToEntity() |> DbCommenter.FromEntity) (Ok sent) "Expected the same commenter"
    }

    test "a row from before the resend rule reads with no date" {
      let entity = commenter.ToEntity()
      entity.Remove "ValidationEmailSentDate" |> ignore

      Expect.equal (DbCommenter.FromEntity entity) (Ok commenter) "Expected no date"
    }

    test "a row with a bad date of a validation email is refused" {
      let entity = commenter.ToEntity()
      entity["ValidationEmailSentDate"] <- "yesterday"

      Expect.isError (DbCommenter.FromEntity entity) "Expected a refusal"
    }
  ]
