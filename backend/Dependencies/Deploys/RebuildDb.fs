namespace TwoPoint.Dependencies.Deploys

open TwoPoint.Core.Deploys
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Util

open Azure.Data.Tables
open FsToolkit.ErrorHandling
open IcedTasks
open Microsoft.Extensions.Logging

open System
open System.Globalization

module RebuildRows =

  [<Literal>]
  let Table = "siteRebuilds"

  let tryRebuild (entity : TableEntity) : Rebuild option =
    option {
      let! dispatched = entity.TryGetString "DispatchedDate"
      let number = entity.GetInt64 "RunNumber" |> Option.ofNullable
      let! production = entity.TryGetString "Production" |> Option.bind (fun name -> ProductionDispatch.tryParse name number)

      return
        { DispatchedDate = DateTime.Parse(dispatched, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime()
          Production = production }
    }

/// <summary>
/// The table has one row: the latest "Rebuild site".
/// </summary>
/// <remarks>
/// <c>
/// {
///    "PartitionKey": "site",
///    "RowKey": "latest",
///    "DispatchedDate": "&lt;date&gt;",
///    "Production": "queued" | "logged" | "failed",
///    "RunNumber": &lt;int64&gt; | null
///  }
/// </c>
/// </remarks>
type RebuildDb(tableServiceClient : TableServiceClient, logger : ILogger) =

  member _.SaveRebuild (rebuild : Rebuild) : DependencyResult<unit> =
    runDependencyWithLogging Dependency.Database logger $"{nameof RebuildDb}.SaveRebuild"
    <| cancellableTaskResult {
      let! ct = CancellableTask.getCancellationToken()
      let! _ = tableServiceClient.CreateTableIfNotExistsAsync(RebuildRows.Table, ct)
      let entity = TableEntity(partitionKey = "site", rowKey = "latest")
      entity["DispatchedDate"] <- rebuild.DispatchedDate.ToString("o", CultureInfo.InvariantCulture)

      entity["Production"] <- ProductionDispatch.name rebuild.Production
      entity["RunNumber"] <- rebuild.Production |> ProductionDispatch.runNumber |> Option.map _.Value |> Option.toNullable
      let! _ = tableServiceClient.GetTableClient(RebuildRows.Table).UpsertEntityAsync(entity, TableUpdateMode.Replace, ct)
      return ()
    }
