module TwoPoint.Dependencies.Tests.CommsTests

open TwoPoint.Core.Shared
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Comms
open TwoPoint.Dependencies.Tests.Fakes

open Azure
open Azure.Communication.Email
open Expecto
open FsToolkit.ErrorHandling
open Microsoft.Extensions.Logging.Abstractions

open System
open System.Threading
open System.Threading.Tasks

let private email =
  { DbEmail.OperationId = "operation-1"
    OperationIdSource = "azure"
    CreatedDate = DateTime(2026, 11, 7, 0, 0, 0, DateTimeKind.Utc)
    Recipient = "reader@example.com"
    Status = "sent"
    StatusReason = None }

let private track createEmail =
  (CommsService.track NullLogger.Instance createEmail email CancellationToken.None).Result

type private UnsetOperation() =
  inherit EmailSendOperation()
  override _.Id = "NOT_SET"

type private UnsetEmailClient() =
  inherit EmailClient()

  override _.SendAsync(_ : WaitUntil, _ : string, _ : string, _ : string, _ : string, _ : string, _ : CancellationToken) =
    Task.FromResult(UnsetOperation() :> EmailSendOperation)

let private now = DateTime(2026, 11, 8, 9, 30, 0, DateTimeKind.Utc)
let private operationGuid = Guid "5e4d3c2b-1a0f-4e9d-8c7b-6a5f4e3d2c1b"

let private ok result = result |> Result.defaultWith (fun error -> failwithf "%A" error)

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

    test "an operation ID of NOT_SET from ACS gets a new ID, and the email row gets the time of the clock" {
      let commsDb = RecordingCommsDb()
      let sender = EmailAddress.create "noreply@twopoint.dev" |> ok
      let comms = CommsService.withDb (UnsetEmailClient()) commsDb (FixedSystem([ operationGuid ], now)) NullLogger.Instance sender
      let message = EmailMessage.create "Subject" "<p>Content</p>" "reader@example.com" |> ok

      let sent = (comms.SendEmail message CancellationToken.None).Result

      Expect.equal (sent |> Result.map string) (Ok (string operationGuid)) "Expected the new ID as the operation ID"
      match commsDb.Emails with
      | [ row ] ->
        Expect.equal row.OperationId (string operationGuid) "Expected the new ID on the email row"
        Expect.equal row.OperationIdSource "twopoint" "Expected the ID to come from the backend"
        Expect.equal row.CreatedDate now "Expected the time of the clock on the email row"
      | rows -> failtestf "Expected one email row, got %A" rows
    }
  ]
