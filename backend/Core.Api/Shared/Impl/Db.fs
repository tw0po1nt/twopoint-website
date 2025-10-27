namespace TwoPoint.Core.Shared.Impl

open Azure.Data.Tables

[<AutoOpen>]
module TableEntityExtensions =
  open FsToolkit.ErrorHandling
  
  type TableEntity with
    member this.TryGetString (key: string) =
      match this.TryGetValue(key) with
      | true, value when value <> null -> Some (value.ToString())
      | _ -> None
      
    member this.RequireString entity key =
      key
      |> this.TryGetString
      |> Validation.requireSome $"{entity}.{key} is required, but was missing from TableEntity with PartitionKey: '{this.PartitionKey}'"

[<RequireQualifiedAccess>]
module Db =
  
  open TwoPoint.Core.Util
  
  open Azure
  open Azure.Data.Tables.FSharp
  open FSharp.Control
  open FsToolkit.ErrorHandling
  open IcedTasks
  open Microsoft.Extensions.Logging
  
  open System
  open System.Net
  
  let private runQuery logger op fn =
    runDependencyWithLogging Dependency.Database logger op fn
    
  let private getTable (tableServiceClient: TableServiceClient) (tableName : string) = cancellableTask {
    let! ct = CancellableTask.getCancellationToken()
    do! tableServiceClient.CreateTableIfNotExistsAsync(tableName, ct) |> Task.ignore
    return tableServiceClient.GetTableClient tableName
  }
  
  /// <summary>
  /// Query an Azure Table for a single entity by row key
  /// </summary>
  /// <param name="tableServiceClient">The service client to use to perform the operation</param>
  /// <param name="logger">A logger for telemetry for the operation</param>
  /// <param name="db">The name of the database - used for telemetry</param>
  /// <param name="table">The table being queried</param>
  /// <param name="onEntity">A function called with the fetched entity if no error occurs and the entity is found</param>
  /// <param name="partition">The partition key of the desired entity</param>
  /// <param name="row">The row key of the desired entity</param>
  let trySingle tableServiceClient logger db table (onEntity: TableEntity -> Validation<'a, 'e>) partition row =
    runQuery logger $"{db}.{table}.trySingle"
    <| cancellableTaskResult {
      let! ct = CancellableTask.getCancellationToken()
      let! table = getTable tableServiceClient table
      
      let query =
        partition
        |> Option.map (fun p -> (eq "PartitionKey" p + eq "RowKey" row))
        |> Option.defaultValue (eq "RowKey" row)
      let! entities =
        table.QueryAsync<TableEntity>(
          query,
          cancellationToken = ct
        )
        |> TaskSeq.toListAsync
      return!
        entities
        |> List.traverseValidationA onEntity
        |> DependencyError.ofValidation Dependency.Database
        |> Result.map List.tryExactlyOne
    }
    
  /// <summary>
  /// Query an Azure Table for a single entity by predicate
  /// </summary>
  /// <param name="tableServiceClient">The service client to use to perform the operation</param>
  /// <param name="logger">A logger for telemetry for the operation</param>
  /// <param name="db">The name of the database - used for telemetry</param>
  /// <param name="table">The table being queried</param>
  /// <param name="onEntity">A function called with the fetched entity if no error occurs and the entity is found</param>
  /// <param name="partition">The partition key of the desired entity</param>
  /// <param name="query">The query used to select the desired entity</param>
  let trySingleQuery tableServiceClient logger db table (onEntity: TableEntity -> Validation<'a, 'e>) partition query =
    runQuery logger $"{db}.{table}.trySingle"
    <| cancellableTaskResult {
      let! ct = CancellableTask.getCancellationToken()
      let! table = getTable tableServiceClient table
      
      let query =
        partition
        |> Option.map (fun p -> (eq "PartitionKey" p + query))
        |> Option.defaultValue query
      let! entities =
        table.QueryAsync<TableEntity>(
          query,
          cancellationToken = ct
        )
        |> TaskSeq.toListAsync
      return!
        entities
        |> List.traverseValidationA onEntity
        |> DependencyError.ofValidation Dependency.Database
        |> Result.map List.tryExactlyOne
    }
    
  /// <summary>
  /// Query an Azure Table for all entities
  /// </summary>
  /// <param name="tableServiceClient">The service client to use to perform the operation</param>
  /// <param name="logger">A logger for telemetry for the operation</param>
  /// <param name="db">The name of the database - used for telemetry</param>
  /// <param name="table">The table being queried</param>
  /// <param name="onEntity">A function called with the fetched entities if no error occurs</param>
  let toList tableServiceClient logger db table (onEntity: TableEntity -> Validation<'a, 'e>) =
    runQuery logger $"{db}.{table}.toList"
    <| cancellableTaskResult {
      let! ct = CancellableTask.getCancellationToken()
      let! table = getTable tableServiceClient table
      let! entities =
        table.QueryAsync<TableEntity>(
          "",
          cancellationToken = ct
        )
        |> TaskSeq.toListAsync
      return!
        entities
        |> List.traverseValidationA onEntity
        |> DependencyError.ofValidation Dependency.Database
    }
    
  /// <summary>
  /// Query an Azure Table for all entities matching the provided filter
  /// </summary>
  /// <param name="tableServiceClient">The service client to use to perform the operation</param>
  /// <param name="logger">A logger for telemetry for the operation</param>
  /// <param name="db">The name of the database - used for telemetry</param>
  /// <param name="table">The table being queried</param>
  /// <param name="onEntity">A function called with the fetched entities if no error occurs</param>
  /// <param name="filter">The filter used to select the desired entities</param>
  let where tableServiceClient logger db table (onEntity: TableEntity -> Validation<'a, 'e>) (filter: Filter) =
    runQuery logger $"{db}.{table}.where"
    <| cancellableTaskResult {
      let! ct = CancellableTask.getCancellationToken()
      let! table = getTable tableServiceClient table
      let! entities =
        table.QueryAsync<TableEntity>(
          filter,
          cancellationToken = ct
        )
        |> TaskSeq.toListAsync
      return!
        entities
        |> List.traverseValidationA onEntity
        |> DependencyError.ofValidation Dependency.Database
    }
  
  /// <summary>
  /// Add an entity to an Azure Table
  /// </summary>
  /// <param name="tableServiceClient">The service client to use to perform the operation</param>
  /// <param name="logger">A logger for telemetry for the operation</param>
  /// <param name="db">The name of the database - used for telemetry</param>
  /// <param name="table">The table into which the new entity is being added</param>
  /// <param name="onError">A function called with an error message if an error occurs</param>
  /// <param name="onEntity">A function called with the newly created entity if no error occurs</param>
  /// <param name="entity">The entity to add to the table</param>
  let add
    tableServiceClient
    (logger : ILogger)
    db
    table
    (onError: string -> DependencyError)
    (onEntity: TableEntity -> Validation<'a, 'e>)
    entity =
      
    let operation = $"{db}.{table}.add"
    runQuery logger operation
    <| cancellableTaskResult {
      let! ct = CancellableTask.getCancellationToken()
      let! tableClient = getTable tableServiceClient table
      
      do! cancellableTaskResult {
        try
          do! tableClient.AddEntityAsync(entity, cancellationToken = ct) |> Task.ignore
        with
          | :? RequestFailedException as ex when ex.Status = int HttpStatusCode.Conflict ->
            return! onError ex.Message |> Error
      }
      
      let trySingleEntity table key onEntity = trySingle tableServiceClient logger db table key onEntity
      
      let! entity = entity.RowKey |> trySingleEntity table onEntity (Some entity.PartitionKey)
      
      match entity with
      | Some entity ->
        return entity
      | None ->
        logger.LogError("{op} failed", operation)
        return! onError "Unable to fetch newly created entity" |> Error
    }
    
  /// <summary>
  /// Update an entity in an Azure Table
  /// </summary>
  /// <param name="tableServiceClient">The service client to use to perform the operation</param>
  /// <param name="logger">A logger for telemetry for the operation</param>
  /// <param name="db">The name of the database - used for telemetry</param>
  /// <param name="table">The table where the updated entity exists</param>
  /// <param name="onError">A function called with an error message if an error occurs</param>
  /// <param name="onEntity">A function called with the updated entity if no error occurs</param>
  /// <param name="entity">The entity to update in the table</param>
  /// <param name="updateEntity">A function to apply updates to the entity</param>
  let update
    tableServiceClient
    (logger : ILogger)
    db
    table
    (onError: string -> DependencyError)
    (onEntity: TableEntity -> Validation<'a, 'e>)
    (entity: TableEntity)
    (updateEntity: TableEntity -> TableEntity) =
      
    let operation = $"{db}.{table}.update"
    runQuery logger operation
    <| cancellableTaskResult {
      let! ct = CancellableTask.getCancellationToken()
      let! tableClient = getTable tableServiceClient table
      
      do! cancellableTaskResult {
        try
          let! entity = tableClient.GetEntityAsync<TableEntity>(entity.PartitionKey, entity.RowKey)
          let entity = updateEntity entity.Value
          do! tableClient.UpdateEntityAsync(entity, entity.ETag, cancellationToken = ct) |> Task.ignore
        with
          | :? RequestFailedException as ex when ex.Status = int HttpStatusCode.Conflict ->
            return! onError ex.Message |> Error
      }
      
      let trySingleEntity table key onEntity = trySingle tableServiceClient logger db table key onEntity
      
      let! entity = entity.RowKey |> trySingleEntity table onEntity (Some entity.PartitionKey)
      
      match entity with
      | Some entity ->
        return entity
      | None ->
        logger.LogError("{op} failed", operation)
        return! onError "Unable to fetch updated entity" |> Error
    }
    
  /// <summary>
  /// Delete entities in an Azure Table
  /// </summary>
  /// <param name="tableServiceClient">The service client to use to perform the operation</param>
  /// <param name="logger">A logger for telemetry for the operation</param>
  /// <param name="db">The name of the database - used for telemetry</param>
  /// <param name="table">The table where the entities to be deleted exist</param>
  /// <param name="onError">A function called with an error message if an error occurs</param>
  /// <param name="filter">The filter used to select the desired entities</param>
  let delete
    tableServiceClient
    (logger : ILogger)
    db
    table
    (onError: string -> DependencyError)
    (filter: Filter) =
      
    let operation = $"{db}.{table}.delete"
    runQuery logger operation
    <| cancellableTask {
      let! ct = CancellableTask.getCancellationToken()
      let! tableClient = getTable tableServiceClient table
      
      let entities = tableClient.QueryAsync<TableEntity>(
        filter,
        cancellationToken = ct
      )
      
      let res =
        taskSeq {
          for entity in entities do
            try
              do! tableClient.DeleteEntityAsync(entity.PartitionKey, entity.RowKey, cancellationToken = ct) |> Task.ignore
              yield Ok ()
            with
              | :? RequestFailedException as ex ->
                yield ex |> Validation.error
        }
        |> TaskSeq.sequenceValidationA
        |> Task.map Result.ignore
        
      match! res with
      | Ok () ->
        return Ok()
      | Error errors ->
        errors |> List.iter (fun ex -> logger.LogError(message = "{op}: An error occurred when deleting an entity", ``exception`` = ex, args = [| operation |]))
        return onError "One or more errors occurred when deleting entities" |> Error
    }
