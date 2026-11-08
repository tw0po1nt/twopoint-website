module TwoPoint.Http.Tests.AuthorReplyMappingTests

open TwoPoint.Core.Posts
open TwoPoint.Http.Endpoints.Admin

open Expecto
open System.Net

[<Tests>]
let tests =
  testList "AuthorReplyMapping" [
    test "an author reply to a rejected comment gets 409" {
      let _, status = AuthorReplyMapping.refused "first-post" "a-comment" AuthorReplyRefusal.ReplyToRejected
      Expect.equal status HttpStatusCode.Conflict "Expected 409"
    }

    test "an author reply to a missing comment gets 404" {
      let _, status = AuthorReplyMapping.refused "first-post" "a-comment" AuthorReplyRefusal.ReplyToMissing
      Expect.equal status HttpStatusCode.NotFound "Expected 404"
    }
  ]
