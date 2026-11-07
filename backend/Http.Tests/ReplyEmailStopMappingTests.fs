module TwoPoint.Http.Tests.ReplyEmailStopMappingTests

open TwoPoint.Core.Posts
open TwoPoint.Core.Shared
open TwoPoint.Http.Endpoints.Blog

open Expecto
open System.Net

let private title = NonEmptyString.Unsafe.create None "A post"

[<Tests>]
let tests =
  testList "ReplyEmailStopMapping" [
    test "each state gets 200 with its name, and only a known token gives the post title" {
      let answers =
        [ ReplyEmailStopState.Active title, { State = "active"; PostTitle = Some "A post" }
          ReplyEmailStopState.Stopped title, { State = "stopped"; PostTitle = Some "A post" }
          ReplyEmailStopState.Unknown, { State = "unknown"; PostTitle = None } ]

      for state, json in answers do
        let response, status = ReplyEmailStopMapping.answer (Ok state)
        Expect.equal status HttpStatusCode.OK $"The status of {state}"
        Expect.equal response.Data (Some json) $"The data of {state}"
    }

    test "a failure gets 500 with its message and no data" {
      let response, status = ReplyEmailStopMapping.answer (Error "The table is down")
      Expect.equal status HttpStatusCode.InternalServerError "Expected 500"
      Expect.equal response.Message (Some "The table is down") "Expected the message"
      Expect.isNone response.Data "Expected no data"
    }
  ]
