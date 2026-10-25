namespace TwoPoint.Dependencies.Deploys

open TwoPoint.Core.Deploys
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

  let eventName (event : RunEvent) =
    match event with
    | Push -> "push"
    | WorkflowDispatch -> "workflow_dispatch"

  let tryEvent (name : string) =
    match name with
    | "push" -> Some Push
    | "workflow_dispatch" -> Some WorkflowDispatch
    | _ -> None

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

  let dateText (date : DateTime) = date.ToString("o", CultureInfo.InvariantCulture)

  let parseDate (text : string) =
    DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime()

  let runEntity (run : SiteRun) =
    let entity = TableEntity(partitionKey = runPartition run.Repo, rowKey = runRow run.Number)
    entity["Number"] <- run.Number.Value
    entity["Event"] <- eventName run.Event
    entity["State"] <- stateName run.State
    entity["CreatedDate"] <- dateText run.CreatedDate
    entity["UpdatedDate"] <- dateText run.UpdatedDate
    entity

  let tryRun (repo : Repo) (entity : TableEntity) : SiteRun option =
    option {
      let! number = entity.GetInt64 "Number" |> Option.ofNullable
      let! event = entity.TryGetString "Event" |> Option.bind tryEvent
      let! state = entity.TryGetString "State" |> Option.bind tryState
      let! created = entity.TryGetString "CreatedDate"
      let! updated = entity.TryGetString "UpdatedDate"

      return
        { Repo = repo
          Number = RunNumber number
          Event = event
          State = state
          CreatedDate = parseDate created
          UpdatedDate = parseDate updated }
    }

  let changeEntity (change : PostChange) =
    let entity = TableEntity(partitionKey = "post", rowKey = Slug.value change.Slug)
    entity["ChangedDate"] <- dateText change.Change.ChangedDate
    entity["Production"] <- ProductionDispatch.name change.Change.Production
    entity["RunNumber"] <- change.Change.Production |> ProductionDispatch.runNumber |> Option.map _.Value |> Option.toNullable
    entity

  let tryChange (entity : TableEntity) : (Slug * Change) option =
    option {
      let! slug = Slug.create entity.RowKey |> Result.toOption
      let! changed = entity.TryGetString "ChangedDate"
      let number = entity.GetInt64 "RunNumber" |> Option.ofNullable
      let! production = entity.TryGetString "Production" |> Option.bind (fun name -> ProductionDispatch.tryParse name number)
      return slug, { ChangedDate = parseDate changed; Production = production }
    }

/// <summary>
/// The table <c>siteRuns</c> has one row for each run of <c>site.yml</c>: the partition is the repo, and the row is
/// the run number. The table <c>postChanges</c> has one row for the latest change of each post.
/// </summary>
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

  member _.SaveRun (run : SiteRun) : DependencyResult<unit> =
    runDependencyWithLogging Dependency.Database logger $"{nameof DeployDb}.SaveRun"
    <| cancellableTaskResult {
      let! ct = CancellableTask.getCancellationToken()
      let! runs = table RunsTable
      let! _ = runs.UpsertEntityAsync(DeployRows.runEntity run, TableUpdateMode.Replace, ct)
      return ()
    }

  member _.GetRun (repo : Repo) (number : RunNumber) : DependencyResult<SiteRun option> =
    runDependencyWithLogging Dependency.Database logger $"{nameof DeployDb}.GetRun"
    <| cancellableTaskResult {
      let! ct = CancellableTask.getCancellationToken()
      let! runs = table RunsTable

      let! response =
        runs.GetEntityIfExistsAsync<TableEntity>(DeployRows.runPartition repo, DeployRows.runRow number, cancellationToken = ct)

      return if response.HasValue then DeployRows.tryRun repo response.Value else None
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
      let! succeeded = runs production " and State eq 'succeeded'"

      return
        { Changes = entities |> List.choose DeployRows.tryChange |> Map.ofList
          Rebuild = if rebuild.HasValue then RebuildRows.tryRebuild rebuild.Value else None
          Runs = { Latest = latest; LatestSucceeded = succeeded } }
    }
