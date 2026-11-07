module TwoPoint.Http.Tests.PostCommentMappingTests

open TwoPoint.Core.Posts
open TwoPoint.Http.Endpoints.Blog

open Expecto
open System.Net

[<Tests>]
let tests =
  testList "PostCommentMapping" [
    test "each reason of the reply check gives 409 with its own message" {
      let reasons =
        [ ReplyRefusal.ReplyToReply
          ReplyRefusal.ReplyToNew
          ReplyRefusal.ReplyToRejected
          ReplyRefusal.ReplyToMissing
          ReplyRefusal.PostUpcoming ]

      let answers = reasons |> List.map PostCommentMapping.replyRefused

      for (_, status), reason in List.zip answers reasons do
        Expect.equal status HttpStatusCode.Conflict $"The status of {reason}"

      Expect.equal (answers |> List.map fst |> List.distinct |> List.length) reasons.Length "Expected a message for each reason"
    }
  ]
