namespace TwoPoint.Dependencies.Deploys

open TwoPoint.Core.Deploys
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Util

open Azure.Data.Tables
open FsToolkit.ErrorHandling
open IcedTasks
open Microsoft.Extensions.Logging


module RebuildRows =

  [<Literal>]
  let Table = "siteRebuilds"

  let tryRebuild (entity : TableEntity) : Rebuild option =
    ChangeColumns.read "DispatchedDate" entity
    |> Option.map (fun (date, production) -> { DispatchedDate = date; Production = production })

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
      ChangeColumns.write "DispatchedDate" entity rebuild.DispatchedDate rebuild.Production
      let! _ = tableServiceClient.GetTableClient(RebuildRows.Table).UpsertEntityAsync(entity, TableUpdateMode.Replace, ct)
      return ()
    }
