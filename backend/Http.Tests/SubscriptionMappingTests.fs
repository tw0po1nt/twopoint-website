module TwoPoint.Http.Tests.SubscriptionMappingTests

open TwoPoint.Core.Shared
open TwoPoint.Core.Subscriptions
open TwoPoint.Core.Subscriptions.Actions
open TwoPoint.Core.Util
open TwoPoint.Http.Endpoints.Blog

open Expecto

open System
open System.Net

let private subscription status : Subscription =
  { Id = SubscriptionId.ofGuid (Guid "2f1c4ed9-6a5b-4f0e-9a43-3d2b8c1e7a10")
    CreatedDate = DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    EmailAddress = EmailAddress.Unsafe.create "reader@example.com"
    Name = None
    Status = status }

let private pending = subscription (SubscriptionStatus.Pending (DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)))

let private tableDown = { Dependency = Dependency.Database; Type = DependencyErrorType.Validation "The table is down" }

[<Tests>]
let tests =
  testList "Subscription mappings" [
    test "a new subscription, a new request, and each refusal of the logic get the same 200" {
      let results : ActionResult<CreateSubscription.Event, CreateSubscription.Error> list =
        [ Ok (CreateSubscription.Event.Created pending)
          Ok (CreateSubscription.Event.Requested pending)
          Error (Logic (CreateSubscription.Error.AlreadyActive (subscription Active)))
          Error (Logic (CreateSubscription.Error.ConfirmationRecent pending)) ]

      for result in results do
        let response, status = SubscribeMapping.answer result
        Expect.equal status HttpStatusCode.OK $"The status of %A{result}"
        Expect.equal response { Success = true; Message = None; Data = None } $"The answer of %A{result}"
    }

    test "a bad address gets 400, and a failed dependency gets 500" {
      let _, invalid = SubscribeMapping.answer (Error (ActionError.Validation [ "'emailAddress' must be an email address" ]))
      let _, failed = SubscribeMapping.answer (Error (ActionError.Dependency tableDown))
      Expect.equal invalid HttpStatusCode.BadRequest "Expected 400"
      Expect.equal failed HttpStatusCode.InternalServerError "Expected 500"
    }

    test "each confirm event gets 200 with its state" {
      let answers =
        [ ConfirmSubscription.Event.Activated (subscription Active), "confirmed"
          ConfirmSubscription.Event.NoChange SubscriptionConfirmState.Confirmed, "confirmed"
          ConfirmSubscription.Event.NoChange SubscriptionConfirmState.Unknown, "unknown" ]

      for event, state in answers do
        let response, status = SubscriptionConfirmMapping.answer (Ok event)
        Expect.equal status HttpStatusCode.OK $"The status of %A{event}"
        Expect.equal response.Data (Some { State = state }) $"The state of %A{event}"
    }

    test "a failed confirm gets 500 with its message and no data" {
      let response, status = SubscriptionConfirmMapping.answer (Error tableDown)
      Expect.equal status HttpStatusCode.InternalServerError "Expected 500"
      Expect.equal response.Message (Some "The table is down") "Expected the message"
      Expect.isNone response.Data "Expected no data"
    }
  ]
