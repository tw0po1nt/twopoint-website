module TwoPoint.Dependencies.Tests.DeliveryStartRowsTests

open TwoPoint.Core.Posts
open TwoPoint.Dependencies.Subscriptions

open Expecto

open System

[<Tests>]
let tests =
  testList "DeliveryStartRows" [
    test "a row keeps the slug of the post and the UTC date of the start" {
      let startedDate = DateTime(2026, 10, 30, 12, 34, 56, 789, DateTimeKind.Utc)
      let entity = DeliveryStartRows.entity (Slug.Unsafe.create "my-post") startedDate

      Expect.equal entity.PartitionKey DeliveryStartRows.Partition "The partition"
      Expect.equal entity.RowKey "my-post" "The slug"

      let read = DeliveryStartRows.startedDate entity
      Expect.equal read startedDate "The date"
      Expect.equal read.Kind DateTimeKind.Utc "UTC"
    }
  ]
