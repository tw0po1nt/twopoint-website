module TwoPoint.Dependencies.Tests.CommsTests

open TwoPoint.Core.Util
open TwoPoint.Dependencies.Comms

open Expecto
open FsToolkit.ErrorHandling
open Microsoft.Extensions.Logging.Abstractions

open System
open System.Threading

let private email =
  { DbEmail.OperationId = "operation-1"
    OperationIdSource = "azure"
    CreatedDate = DateTime(2026, 11, 7, 0, 0, 0, DateTimeKind.Utc)
    Recipient = "reader@example.com"
    Status = "sent"
    StatusReason = None }

let private track createEmail =
  (CommsService.track NullLogger.Instance createEmail email CancellationToken.None).Result

[<Tests>]
let tests =
  testList "Comms" [
    test "the tracking row gives the operation ID of the sent email" {
      let created = track (fun row -> CancellableTaskResult.singleton row)
      Expect.equal created "operation-1" "Expected the operation ID"
    }

    test "a failed tracking row still gives the operation ID, so a retry sends no second email" {
      let failed =
        track (fun _ -> CancellableTaskResult.error { Dependency = Dependency.Database; Type = Unknown (exn "table down") })

      Expect.equal failed "operation-1" "Expected the operation ID and no error"
    }
  ]
