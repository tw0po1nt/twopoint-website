module TwoPoint.Dependencies.Tests.SubscriptionDbTests

open TwoPoint.Core.Subscriptions
open TwoPoint.Dependencies.Subscriptions

open Azure.Data.Tables
open Expecto

open System

let private created = DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
let private written = DateTimeOffset(2026, 1, 20, 8, 30, 0, TimeSpan.Zero)
let private stored = DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc)

let private entityWith (isActive : bool) (dates : (string * DateTime) list) =
  let entity = TableEntity(partitionKey = "2f1c4ed9-6a5b-4f0e-9a43-3d2b8c1e7a10", rowKey = "2f1c4ed9-6a5b-4f0e-9a43-3d2b8c1e7a10")
  entity["CreatedDate"] <- created.ToString "o"
  entity["EmailAddress"] <- "reader@example.com"
  entity["IsActive"] <- isActive
  for key, date in dates do
    entity[key] <- date.ToString "o"
  entity.Timestamp <- Nullable written
  entity

let private entity (isActive : bool) (canceledDate : DateTime option) =
  entityWith isActive (canceledDate |> Option.map (fun date -> "CanceledDate", date) |> Option.toList)

let private row (entity : TableEntity) =
  match DbSubscription.FromEntity entity with
  | Ok subscription -> subscription
  | Error errors -> failtestf "Expected a subscription, got %A" errors

let private status (entity : TableEntity) =
  match (row entity).ToSubscription() with
  | Ok subscription -> subscription.Status
  | Error errors -> failtestf "Expected a core subscription, got %A" errors

[<Tests>]
let tests =
  testList "DbSubscription" [
    test "an inactive row with no pending date and no canceled date is an error" {
      match DbSubscription.FromEntity (entity false None) with
      | Ok subscription -> failtestf "Expected an error, got %A" subscription
      | Error errors ->
        Expect.contains
          errors
          "DbSubscription.CanceledDate is required, but was missing from TableEntity with PartitionKey: '2f1c4ed9-6a5b-4f0e-9a43-3d2b8c1e7a10'"
          "Expected an error that names CanceledDate and the PartitionKey"
    }

    test "an inactive row keeps its canceled date, and its timestamp has no effect" {
      Expect.equal (status (entity false (Some stored))) (Canceled stored) "Expected the stored date"
    }

    test "a row maps to the status of the core subscription" {
      Expect.equal (status (entity true None)) Active "Active"
      Expect.equal (status (entity false (Some stored))) (Canceled stored) "Canceled"
      Expect.equal (status (entityWith false [ "PendingDate", stored ])) (SubscriptionStatus.Pending stored) "Pending"
    }

    test "an active row is active, also with a stale canceled or pending date" {
      Expect.equal (status (entity true (Some stored))) Active "Stale canceled date"
      Expect.equal (status (entityWith true [ "PendingDate", stored ])) Active "Stale pending date"
    }

    test "a pending date that is not a date is an error" {
      let bad = entity false None
      bad["PendingDate"] <- "soon"
      Expect.isError (DbSubscription.FromEntity bad) "Expected an error"
    }

    test "each status writes the columns that read back as that status" {
      for subscriptionStatus in [ Active; SubscriptionStatus.Pending stored; Canceled stored ] do
        let written = { row (entity true None) with Status = subscriptionStatus }.ToEntity()
        Expect.equal (status written) subscriptionStatus $"The status {subscriptionStatus} reads back"
    }

    test "a pending row writes its date and no canceled date" {
      let written = { row (entity true None) with Status = SubscriptionStatus.Pending stored }.ToEntity()
      Expect.equal written["IsActive"] (box false) "Expected IsActive false"
      Expect.equal written["PendingDate"] (box (stored.ToString "o")) "Expected the pending date"
      Expect.isNull written["CanceledDate"] "Expected no canceled date"
    }
  ]
