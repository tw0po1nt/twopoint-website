namespace TwoPoint.Dependencies.Notifications

open TwoPoint.Dependencies.Util
open TwoPoint.Core.Notifications
open TwoPoint.Core.Posts
open TwoPoint.Core.Shared
open TwoPoint.Core.Util

open Azure.Data.Tables
open FsToolkit.ErrorHandling

open System
open System.Globalization

/// <summary>
/// Database representation of a registered push device
/// </summary>
/// <remarks>
/// The <c>registeredDevices</c> entity has the following structure: <br/>
/// <c>
/// {
///    "PartitionKey": "&lt;userExternalId&gt;",
///    "RowKey": "&lt;SHA-256 of the endpoint, in hexadecimal&gt;",
///    "Timestamp": "&lt;date&gt;",
///    "Endpoint": "&lt;string&gt;",
///    "P256dh": "&lt;string&gt;",
///    "Auth": "&lt;string&gt;",
///    "Label": "&lt;string&gt;",
///    "CreatedDate": "&lt;date&gt;",
///    "LastSeenDate": "&lt;date&gt;"
///  }
/// </c>
/// </remarks>
type internal DbRegisteredDevice =
  { UserExternalId : string
    DeviceId : string
    Endpoint : string
    P256dh : string
    Auth : string
    Label : string
    CreatedDate : DateTime
    LastSeenDate : DateTime }

  static member FromEntity (entity : TableEntity) = validation {
    let entityName = nameof DbRegisteredDevice
    let! endpoint = "Endpoint" |> entity.RequireString entityName
    and! p256dh = "P256dh" |> entity.RequireString entityName
    and! auth = "Auth" |> entity.RequireString entityName
    and! label = "Label" |> entity.RequireString entityName
    and! createdDate =
      "CreatedDate"
      |> entity.RequireString entityName
      |> Validation.bind DateTime.TryValidateUtc
    and! lastSeenDate =
      "LastSeenDate"
      |> entity.RequireString entityName
      |> Validation.bind DateTime.TryValidateUtc

    return
      { UserExternalId = entity.PartitionKey
        DeviceId = entity.RowKey
        Endpoint = endpoint
        P256dh = p256dh
        Auth = auth
        Label = label
        CreatedDate = createdDate
        LastSeenDate = lastSeenDate }
  }

  member this.ToEntity() =
    let entity = TableEntity(partitionKey = this.UserExternalId, rowKey = this.DeviceId)
    entity["Endpoint"] <- this.Endpoint
    entity["P256dh"] <- this.P256dh
    entity["Auth"] <- this.Auth
    entity["Label"] <- this.Label
    entity["CreatedDate"] <- this.CreatedDate.ToString("o", CultureInfo.InvariantCulture)
    entity["LastSeenDate"] <- this.LastSeenDate.ToString("o", CultureInfo.InvariantCulture)
    entity

  static member FromDevice (device : RegisteredDevice) =
    { UserExternalId = device.UserId.ToString()
      DeviceId = device.Id.ToString()
      Endpoint = device.Endpoint.ToString()
      P256dh = NonEmptyString.value device.P256dh
      Auth = NonEmptyString.value device.Auth
      Label = NonEmptyString.value device.Label
      CreatedDate = device.CreatedDate
      LastSeenDate = device.LastSeenDate }

  member this.ToDevice () = validation {
    let! userId = this.UserExternalId |> UserExternalId.create
    and! deviceId = this.DeviceId |> DeviceId.create
    and! endpoint = this.Endpoint |> PushEndpoint.create
    and! p256dh = this.P256dh |> NonEmptyString.create (Some "P256dh")
    and! auth = this.Auth |> NonEmptyString.create (Some "Auth")
    and! label = this.Label |> NonEmptyString.create (Some "Label")
    return
      { RegisteredDevice.UserId = userId
        Id = deviceId
        Endpoint = endpoint
        P256dh = p256dh
        Auth = auth
        Label = label
        CreatedDate = this.CreatedDate.ToUniversalTime()
        LastSeenDate = this.LastSeenDate.ToUniversalTime() }
  }

type internal IDeviceDb =
  abstract member GetDevicesOfUser:
    userId: string -> CancellableTaskResult<DbRegisteredDevice list, DependencyError>
  abstract member GetDevice:
    userId: string -> deviceId: string -> CancellableTaskResult<DbRegisteredDevice option, DependencyError>
  abstract member SaveDevice: device: DbRegisteredDevice -> CancellableTaskResult<DbRegisteredDevice, DependencyError>
  abstract member DeleteDevice: device: DbRegisteredDevice -> CancellableTaskResult<unit, DependencyError>

module internal DeviceDb =

  open Azure.Data.Tables.FSharp
  open Microsoft.Extensions.Logging

  let private validation message =
    { Dependency = Dependency.Database; Type = DependencyErrorType.Validation message }

  let live (tableServiceClient : TableServiceClient) (logger : ILogger) =

    let db = nameof IDeviceDb
    let table = "registeredDevices"

    { new IDeviceDb with

      member _.GetDevicesOfUser userId =
        eq "PartitionKey" userId |> Db.where tableServiceClient logger db table DbRegisteredDevice.FromEntity

      member _.GetDevice userId deviceId =
        deviceId |> Db.trySingle tableServiceClient logger db table DbRegisteredDevice.FromEntity (Some userId)

      member _.SaveDevice device =
        device.ToEntity() |> Db.upsert tableServiceClient logger db table validation DbRegisteredDevice.FromEntity

      member _.DeleteDevice device =
        (eq "PartitionKey" device.UserExternalId + eq "RowKey" device.DeviceId)
        |> Db.delete tableServiceClient logger db table validation
    }
