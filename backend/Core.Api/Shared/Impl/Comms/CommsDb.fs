namespace TwoPoint.Core.Shared.Impl

open TwoPoint.Core.Shared.Impl
open TwoPoint.Core.Util

open Azure.Data.Tables
open FsToolkit.ErrorHandling

open System
open System.Globalization

/// <summary>
/// Database representation of an email
/// </summary>
/// <remarks>
/// The <c>Post</c> entity has the following structure: <br/>
/// <c>
/// {
///    "PartitionKey": "&lt;operationId&gt;",
///    "RowKey": "&lt;operationId&gt;",
///    "Timestamp": "&lt;date&gt;",
///    "OperationIdSource": "&lt;azure | twopoint&gt;",
///    "CreatedDate": "&lt;date&gt;",
///    "Recipient": "&lt;email&gt;",
///    "Status": "&lt;sent | delivered | failed&gt;"
///    "StatusReason" "&lt;string | null&gt;"
///  }
/// </c>
/// </remarks>
type internal DbEmail =
  { OperationId : string
    OperationIdSource : string
    CreatedDate : DateTime
    Recipient : string
    Status : string
    StatusReason : string option }
  
  static member FromEntity (entity : TableEntity) = validation {
    let entityName = nameof DbEmail
    let! operationIdSource = "OperationIdSource" |> entity.RequireString entityName
    let! createdDate =
      "CreatedDate"
      |> entity.RequireString entityName
      |> Validation.bind DateTime.TryValidateUtc
    let! recipient = "Recipient" |> entity.RequireString entityName
    let! status = "Status" |> entity.RequireString entityName
    let statusReason = "StatusReason" |> entity.TryGetString

    return
      { OperationId = entity.RowKey
        OperationIdSource = operationIdSource
        CreatedDate =  createdDate
        Recipient = recipient
        Status = status
        StatusReason = statusReason }
  }
  
  member this.ToEntity() =
    let entity = TableEntity(partitionKey = this.OperationId, rowKey = this.OperationId)
    entity["OperationIdSource"] <- this.OperationIdSource
    entity["CreatedDate"] <- this.CreatedDate.ToString("o", CultureInfo.InvariantCulture)
    entity["Recipient"] <- this.Recipient
    entity["Status"] <- this.Status
    entity["StatusReason"] <- this.StatusReason |> Option.toObj
    entity

type internal ICommsDb =
  abstract member GetEmailByOperationId: operationId: string -> CancellableTaskResult<DbEmail option, DependencyError>
  abstract member CreateEmail: post: DbEmail -> CancellableTaskResult<DbEmail, DependencyError>
  abstract member UpdateEmailStatus:
    commenter: DbEmail
      -> status: string
      -> statusReason: string option
      -> CancellableTaskResult<DbEmail, DependencyError>


module internal CommsDbError =
  let private commsDbError =
    { Dependency = Dependency.Database
      Type = Unknown (exn "") }
    
  let validation msg = { commsDbError with Type = DependencyErrorType.Validation msg }
    
  let unknown msg = { commsDbError with Type = msg |> FSharp.Core.exn |> DependencyErrorType.Unknown }


module internal CommsDb =
  
  open Microsoft.Extensions.Logging
  
  let live (tableServiceClient : TableServiceClient) (logger : ILogger) =

    let db = nameof ICommsDb

    let trySingle table onEntity partition key =
      Db.trySingle tableServiceClient logger db table onEntity partition key
      
    let add table onError onEntity entity =
      Db.add tableServiceClient logger db table onError onEntity entity
      
    let update table onError onEntity entity updateEntity =
      Db.update tableServiceClient logger db table onError onEntity entity updateEntity
      
    let emailsTable = "emails"
    
    { new ICommsDb with
      
      member this.GetEmailByOperationId operationId =
        operationId |> trySingle emailsTable DbEmail.FromEntity (Some operationId)

      member this.CreateEmail email =
        email.ToEntity() |> add emailsTable CommsDbError.validation DbEmail.FromEntity

      member this.UpdateEmailStatus commenter status statusReason =
        commenter.ToEntity() |> update emailsTable CommsDbError.validation DbEmail.FromEntity
        <| fun entity ->
          entity["Status"] <- status
          entity["StatusReason"] <- statusReason |> Option.toObj
          entity
    }
  
