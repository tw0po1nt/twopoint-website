namespace TwoPoint.Dependencies.Deploys

open TwoPoint.Core.Deploys
open TwoPoint.Core.Deploys.Actions
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

module DeployRows =

  let runPartition (repo : Repo) = $"{repo.Owner}:{repo.Name}"

  /// A greater run number has a smaller row key, so a query reads the newest run first.
  let runRow (number : RunNumber) = (Int64.MaxValue - number.Value).ToString("D19", CultureInfo.InvariantCulture)

  let stateName (state : RunState) =
    match state with
    | RunState.Waiting -> "waiting"
    | RunState.Running -> "running"
    | RunState.Succeeded -> "succeeded"
    | RunState.Failed -> "failed"
    | RunState.Canceled -> "canceled"

  let tryState (name : string) =
    match name with
    | "waiting" -> Some RunState.Waiting
    | "running" -> Some RunState.Running
    | "succeeded" -> Some RunState.Succeeded
    | "failed" -> Some RunState.Failed
    | "canceled" -> Some RunState.Canceled
    | _ -> None

  let runEntity (run : SiteRun) =
    let entity = TableEntity(partitionKey = runPartition run.Repo, rowKey = runRow run.Number)
    entity["Number"] <- run.Number.Value
    entity["Event"] <- RunEvent.name run.Event
    entity["State"] <- stateName run.State
    entity["CreatedDate"] <- ChangeColumns.dateText run.CreatedDate
    entity["UpdatedDate"] <- ChangeColumns.dateText run.UpdatedDate
    entity

  let tryRun (repo : Repo) (entity : TableEntity) : SiteRun option =
    option {
      let! number = entity.GetInt64 "Number" |> Option.ofNullable
      let! event = entity.TryGetString "Event" |> Option.bind RunEvent.tryParse
      let! state = entity.TryGetString "State" |> Option.bind tryState
      let! created = entity.TryGetString "CreatedDate"
      let! updated = entity.TryGetString "UpdatedDate"

      return
        { Repo = repo
          Number = RunNumber number
          Event = event
          State = state
          CreatedDate = ChangeColumns.parseDate created
          UpdatedDate = ChangeColumns.parseDate updated }
    }

  let changeEntity (change : PostChange) =
    let entity = TableEntity(partitionKey = "post", rowKey = Slug.value change.Slug)
    ChangeColumns.write "ChangedDate" entity change.Change.ChangedDate change.Change.Production
    entity

  let tryChange (entity : TableEntity) : (Slug * Change) option =
    option {
      let! slug = Slug.create entity.RowKey |> Result.toOption
      let! changedDate, production = ChangeColumns.read "ChangedDate" entity
      return slug, { ChangedDate = changedDate; Production = production }
    }

type DeployDb(tableServiceClient : TableServiceClient, logger : ILogger) =

  [<Literal>]
  let RunsTable = "siteRuns"

  [<Literal>]
  let ChangesTable = "postChanges"

  let table name = cancellableTask {
    let! ct = CancellableTask.getCancellationToken()
    let! _ = tableServiceClient.CreateTableIfNotExistsAsync(name, ct)
    return tableServiceClient.GetTableClient name
  }

  let runs (repo : Repo) extraFilter = cancellableTask {
    let! ct = CancellableTask.getCancellationToken()
    let! runs = table RunsTable
    let partition = DeployRows.runPartition repo
    let filter = $"PartitionKey eq '{partition}'{extraFilter}"
    let! first = runs.QueryAsync<TableEntity>(filter, maxPerPage = 1, cancellationToken = ct) |> TaskSeq.tryHead
    return first |> Option.bind (DeployRows.tryRun repo)
  }

  member _.SaveRun (incoming : SiteRun) : DependencyResult<ReceiveWorkflowRun.Next> =
    runDependencyWithLogging Dependency.Database logger $"{nameof DeployDb}.SaveRun"
    <| cancellableTaskResult {
      let! ct = CancellableTask.getCancellationToken()
      let! runs = table RunsTable
      let partition = DeployRows.runPartition incoming.Repo
      let row = DeployRows.runRow incoming.Number

      let mutable outcome = None
      let mutable remaining = 5

      while outcome.IsNone do
        let! response = runs.GetEntityIfExistsAsync<TableEntity>(partition, row, cancellationToken = ct)
        let stored = if response.HasValue then DeployRows.tryRun incoming.Repo response.Value else None

        match ReceiveWorkflowRun.Logic.next stored incoming with
        | ReceiveWorkflowRun.Next.OlderThanStored -> outcome <- Some ReceiveWorkflowRun.Next.OlderThanStored
        | ReceiveWorkflowRun.Newer run ->
          try
            if response.HasValue then
              let! _ = runs.UpdateEntityAsync(DeployRows.runEntity run, response.Value.ETag, TableUpdateMode.Replace, ct)
              ()
            else
              let! _ = runs.AddEntityAsync(DeployRows.runEntity run, ct)
              ()

            outcome <- Some(ReceiveWorkflowRun.Newer run)
          with :? RequestFailedException as ex when (ex.Status = 409 || ex.Status = 412) && remaining > 1 ->
            remaining <- remaining - 1

      return outcome.Value
    }

  member _.RecordChange (change : PostChange) : DependencyResult<unit> =
    runDependencyWithLogging Dependency.Database logger $"{nameof DeployDb}.RecordChange"
    <| cancellableTaskResult {
      let! ct = CancellableTask.getCancellationToken()
      let! changes = table ChangesTable
      let! _ = changes.UpsertEntityAsync(DeployRows.changeEntity change, TableUpdateMode.Replace, ct)
      return ()
    }

  member _.GetDeployContext (production : Repo) : DependencyResult<DeployContext> =
    runDependencyWithLogging Dependency.Database logger $"{nameof DeployDb}.GetDeployContext"
    <| cancellableTaskResult {
      let! ct = CancellableTask.getCancellationToken()
      let! changes = table ChangesTable
      let! rebuilds = table RebuildRows.Table

      let! entities = changes.QueryAsync<TableEntity>("PartitionKey eq 'post'", cancellationToken = ct) |> TaskSeq.toListAsync
      let! rebuild = rebuilds.GetEntityIfExistsAsync<TableEntity>("site", "latest", cancellationToken = ct)
      let! latest = runs production ""
      let! succeeded = runs production $" and State eq '{DeployRows.stateName RunState.Succeeded}'"

      return
        { Changes = entities |> List.choose DeployRows.tryChange |> Map.ofList
          Rebuild = if rebuild.HasValue then RebuildRows.tryRebuild rebuild.Value else None
          Runs = { Latest = latest; LatestSucceeded = succeeded } }
    }
