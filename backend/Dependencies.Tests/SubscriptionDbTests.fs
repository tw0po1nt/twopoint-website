module TwoPoint.Dependencies.Tests.SubscriptionDbTests

open TwoPoint.Core.Subscriptions
open TwoPoint.Dependencies.Subscriptions

open Azure.Data.Tables
open Expecto

open System

let private created = DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
let private written = DateTimeOffset(2026, 1, 20, 8, 30, 0, TimeSpan.Zero)
let private stored = DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc)

let private entity (isActive : bool) (canceledDate : DateTime option) =
  let entity = TableEntity(partitionKey = "2f1c4ed9-6a5b-4f0e-9a43-3d2b8c1e7a10", rowKey = "2f1c4ed9-6a5b-4f0e-9a43-3d2b8c1e7a10")
  entity["CreatedDate"] <- created.ToString "o"
  entity["EmailAddress"] <- "reader@example.com"
  entity["IsActive"] <- isActive
  canceledDate |> Option.iter (fun date -> entity["CanceledDate"] <- date.ToString "o")
  entity.Timestamp <- Nullable written
  entity

let private canceledDate (entity : TableEntity) =
  match DbSubscription.FromEntity entity with
  | Ok subscription -> subscription.CanceledDate
  | Error errors -> failtestf "Expected a subscription, got %A" errors

[<Tests>]
let tests =
  testList "DbSubscription.FromEntity" [
    test "an inactive row with no canceled date gets the date of its last write" {
      Expect.equal (canceledDate (entity false None)) (Some written.UtcDateTime) "Expected the timestamp"
    }

    test "an inactive row keeps its canceled date" {
      Expect.equal (canceledDate (entity false (Some stored))) (Some stored) "Expected the stored date"
    }

    test "a row maps to the status of the core subscription" {
      let status (entity : TableEntity) =
        match DbSubscription.FromEntity entity with
        | Ok row ->
          match row.ToSubscription() with
          | Ok subscription -> subscription.Status
          | Error errors -> failtestf "Expected a core subscription, got %A" errors
        | Error errors -> failtestf "Expected a subscription, got %A" errors

      Expect.equal (status (entity true None)) Active "Active"
      Expect.equal (status (entity false (Some stored))) (Canceled stored) "Canceled"
      Expect.equal
        (status (entity false None))
        (Canceled written.UtcDateTime)
        "Canceled at the timestamp of the row"
    }

    test "an active row has no canceled date" {
      Expect.isNone (canceledDate (entity true None)) "Expected no date"
      Expect.isNone (canceledDate (entity true (Some stored))) "Expected no date for a stale value"
    }
  ]
