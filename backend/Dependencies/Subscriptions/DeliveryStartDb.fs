namespace TwoPoint.Dependencies.Subscriptions

open TwoPoint.Core.Posts
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Util

open Azure
open Azure.Data.Tables
open FSharp.Control
open FsToolkit.ErrorHandling
open IcedTasks
open Microsoft.Extensions.Logging

open System
open System.Globalization

module DeliveryStartRows =

  [<Literal>]
  let Partition = "post"

  let entity (post : Slug) (startedDate : DateTime) =
    let entity = TableEntity(partitionKey = Partition, rowKey = Slug.value post)
    entity["StartedDate"] <- startedDate.ToString("o", CultureInfo.InvariantCulture)
    entity

  let startedDate (entity : TableEntity) =
    DateTime.Parse(entity.GetString "StartedDate", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
      .ToUniversalTime()

type DeliveryStartDb(tableServiceClient : TableServiceClient, logger : ILogger) =

  [<Literal>]
  let StartsTable = "deliveryStarts"

  let table () = cancellableTask {
    let! ct = CancellableTask.getCancellationToken()
    let! _ = tableServiceClient.CreateTableIfNotExistsAsync(StartsTable, ct)
    return tableServiceClient.GetTableClient StartsTable
  }

  let start (post : Slug) = cancellableTask {
    let! ct = CancellableTask.getCancellationToken()
    let! starts = table ()
    let! row = starts.GetEntityIfExistsAsync<TableEntity>(DeliveryStartRows.Partition, Slug.value post, cancellationToken = ct)
    return if row.HasValue then Started (DeliveryStartRows.startedDate row.Value) else NotStarted
  }

  member _.GetDeliveryStart (post : Slug) : DependencyResult<DeliveryStart> =
    runDependencyWithLogging Dependency.Database logger $"{nameof DeliveryStartDb}.GetDeliveryStart"
    <| cancellableTaskResult { return! start post }

  member _.GetDeliveryStarts () : DependencyResult<Map<Slug, DateTime>> =
    runDependencyWithLogging Dependency.Database logger $"{nameof DeliveryStartDb}.GetDeliveryStarts"
    <| cancellableTaskResult {
      let! ct = CancellableTask.getCancellationToken()
      let! starts = table ()
      let! rows =
        starts.QueryAsync<TableEntity>($"PartitionKey eq '{DeliveryStartRows.Partition}'", cancellationToken = ct)
        |> TaskSeq.toListAsync

      return
        rows
        |> List.choose (fun row ->
          Slug.create row.RowKey |> Result.toOption |> Option.map (fun slug -> slug, DeliveryStartRows.startedDate row))
        |> Map.ofList
    }

  member _.InsertDeliveryStart (post : Slug) (startedDate : DateTime) : DependencyResult<DeliveryStartInsert> =
    runDependencyWithLogging Dependency.Database logger $"{nameof DeliveryStartDb}.InsertDeliveryStart"
    <| cancellableTaskResult {
      let! ct = CancellableTask.getCancellationToken()
      let! starts = table ()

      let mutable outcome = None

      while outcome.IsNone do
        let! inserted = cancellableTask {
          try
            let! _ = starts.AddEntityAsync(DeliveryStartRows.entity post startedDate, ct)
            return Some Inserted
          with :? RequestFailedException as ex when ex.Status = 409 ->
            return None
        }

        match inserted with
        | Some _ -> outcome <- inserted
        | None ->
          // The other start deletes its row after a failed queue, and then the next pass inserts.
          match! start post with
          | Started startedDate -> outcome <- Some (StartedBefore startedDate)
          | NotStarted -> ()

      return outcome.Value
    }

  member _.DeleteDeliveryStart (post : Slug) : DependencyResult<unit> =
    runDependencyWithLogging Dependency.Database logger $"{nameof DeliveryStartDb}.DeleteDeliveryStart"
    <| cancellableTaskResult {
      let! ct = CancellableTask.getCancellationToken()
      let! starts = table ()
      let! _ = starts.DeleteEntityAsync(DeliveryStartRows.Partition, Slug.value post, cancellationToken = ct)
      return ()
    }
