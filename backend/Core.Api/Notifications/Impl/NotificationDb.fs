namespace TwoPoint.Core.Notifications.Dependencies

open TwoPoint.Core.Posts
open TwoPoint.Core.Shared.Impl
open TwoPoint.Core.Util

open Azure.Data.Tables
open FsToolkit.ErrorHandling

open System
open System.Globalization

/// <summary>
/// Database representation of a device registration
/// </summary>
/// <remarks>
/// The <c>DeviceRegistration</c> entity has the following structure: <br/>
/// <c>
/// {
///    "PartitionKey": "&lt;userExternalId&gt;",
///    "RowKey": "&lt;guid&gt;",
///    "Timestamp": "&lt;date&gt;",    
///    "CreatedDate": "&lt;date&gt;",
///    "Client": "&lt;android | ios | ...&gt;",
///    "Token": "&lt;string&gt;"
///  }
/// </c>
/// </remarks>
type internal DbDeviceRegistration =
  { Id : string
    UserExternalId : string
    Client : string
    Token : string
    CreatedDate : DateTime }
  
  static member FromEntity (entity : TableEntity) = validation {
    let entityName = nameof DbDeviceRegistration
    let id = entity.RowKey
    let userExternalId = entity.PartitionKey
    let! client = "Client" |> entity.RequireString entityName
    let! token = "Token" |> entity.RequireString entityName
    let! createdDate =
      "CreatedDate"
      |> entity.RequireString entityName
      |> Validation.bind DateTime.TryValidateUtc

    return
      { DbDeviceRegistration.Id = id
        UserExternalId = userExternalId
        Client = client
        Token = token
        CreatedDate =  createdDate }
  }
  
  member this.ToEntity() =
    let entity = TableEntity(partitionKey = this.UserExternalId, rowKey = this.Id)
    entity["Client"] <- this.Client
    entity["Token"] <- this.Token
    entity["CreatedDate"] <- this.CreatedDate.ToString("o", CultureInfo.InvariantCulture)
    entity
    
  static member FromDeviceRegistration (registration : DeviceRegistration) =
    { Id = registration.Id.ToString()
      UserExternalId = registration.UserExternalId.ToString()
      Client = registration.Client.ToString()
      Token = registration.Token.ToString()
      CreatedDate = registration.CreatedDate }
    
  member this.ToDeviceRegistration() =
    DeviceRegistration.create
      this.Id
      this.UserExternalId
      this.Client
      this.Token
      (this.CreatedDate.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture))

type internal INotificationDb =
  abstract member GetDeviceRegistrationByUserAndClient:
    userExternalId: string
      -> client: string
      -> CancellableTaskResult<DbDeviceRegistration option, DependencyError>
  abstract member CreateDeviceRegistration: registration: DbDeviceRegistration -> CancellableTaskResult<DbDeviceRegistration, DependencyError>
  abstract member UpdateDeviceRegistration:
    registration: DbDeviceRegistration
      -> token: string
      -> CancellableTaskResult<DbDeviceRegistration, DependencyError>
  abstract member RemoveDeviceRegistrationByToken:
    token: string -> CancellableTaskResult<unit, DependencyError>


module internal NotificationDbError =
  let private notificationDbError =
    { Dependency = Dependency.Database
      Type = Unknown (exn "") }
    
  let validation msg = { notificationDbError with Type = DependencyErrorType.Validation msg }
    
  let unknown msg = { notificationDbError with Type = msg |> FSharp.Core.exn |> DependencyErrorType.Unknown }


module internal NotificationDb =
  
  open Azure.Data.Tables.FSharp
  open Microsoft.Extensions.Logging
  
  let live (tableServiceClient : TableServiceClient) (logger : ILogger) =

    let db = nameof INotificationDb
      
    let trySingleQuery table onEntity partition query =
      Db.trySingleQuery tableServiceClient logger db table onEntity partition query
      
    let add table onError onEntity entity =
      Db.add tableServiceClient logger db table onError onEntity entity
      
    let update table onError onEntity entity updateEntity =
      Db.update tableServiceClient logger db table onError onEntity entity updateEntity
      
    let delete table onError filter =
      Db.delete tableServiceClient logger db table onError filter
      
    let deviceRegistrationsTable = "deviceRegistrations"
    
    { new INotificationDb with
      
      member this.GetDeviceRegistrationByUserAndClient userExternalId client =
        (eq "PartitionKey" userExternalId + eq "Client" client) |> trySingleQuery deviceRegistrationsTable DbDeviceRegistration.FromEntity None

      member this.CreateDeviceRegistration registration =
        registration.ToEntity() |> add deviceRegistrationsTable NotificationDbError.validation DbDeviceRegistration.FromEntity

      member this.UpdateDeviceRegistration registration token =
        registration.ToEntity() |> update deviceRegistrationsTable NotificationDbError.validation DbDeviceRegistration.FromEntity
        <| fun entity ->
          entity["Token"] <- token
          entity
          
      member this.RemoveDeviceRegistrationByToken token = 
        (eq "Token" token) |> delete deviceRegistrationsTable NotificationDbError.validation
    }
  
