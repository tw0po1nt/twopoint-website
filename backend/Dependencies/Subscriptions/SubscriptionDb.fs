namespace TwoPoint.Dependencies.Subscriptions

open TwoPoint.Dependencies.Util
open TwoPoint.Core.Posts
open TwoPoint.Core.Subscriptions
open TwoPoint.Core.Shared
open TwoPoint.Core.Util

open Azure.Data.Tables
open FsToolkit.ErrorHandling

open System
open System.Globalization

/// <summary>
/// Database representation of a subscription 
/// </summary>
/// <remarks>
/// The <c>Subscription</c> entity has the following structure: <br/>
/// <c>
/// {
///    "PartitionKey": "&lt;subscriptionId&gt;",
///    "RowKey": "&lt;subscriptionId&gt;",
///    "Timestamp": "&lt;date&gt;",    
///    "CreatedDate": "&lt;date&gt;",
///    "EmailAddress": "&lt;string&gt;",
///    "Name": "&lt;string&gt;" | null,
///    "IsActive": "&lt;boolean&gt;"
///  }
/// </c>
/// </remarks>
type internal DbSubscription =
  { SubscriptionId : string
    CreatedDate : DateTime 
    EmailAddress : string
    Name : string option
    IsActive : bool }
  
  static member FromEntity (entity : TableEntity) = validation {
    let entityName = nameof DbSubscription 
    let! createdDate =
      "CreatedDate"
      |> entity.RequireString entityName
      |> Validation.bind DateTime.TryValidateUtc
    let! emailAddress = "EmailAddress" |> entity.RequireString entityName
    let name = "Name" |> entity.TryGetString
    let isActive = "IsActive" |> entity.TryGetBoolean |> Option.defaultValue false

    return
      { SubscriptionId = entity.RowKey
        CreatedDate =  createdDate
        EmailAddress = emailAddress
        Name = name 
        IsActive = isActive }
  }
  
  member this.ToEntity() =
    let entity = TableEntity(partitionKey = this.SubscriptionId, rowKey = this.SubscriptionId)
    entity["CreatedDate"] <- this.CreatedDate.ToString("o", CultureInfo.InvariantCulture)
    entity["EmailAddress"] <- this.EmailAddress
    entity["Name"] <- this.Name |> Option.toObj
    entity["IsActive"] <- this.IsActive
    entity
    
  static member FromSubscription (subscription : Subscription) =
    { SubscriptionId = subscription.Id.ToString()
      CreatedDate = subscription.CreatedDate
      EmailAddress = subscription.EmailAddress |> EmailAddress.value
      Name = subscription.Name |> Option.map NonEmptyString.value
      IsActive = subscription.IsActive }
    
  member this.ToSubscription () = validation {
    let! subscriptionId = this.SubscriptionId |> SubscriptionId.create
    and! createdDate = DateTime.TryValidateUtc(
      this.CreatedDate.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
      "CreatedDate"
    )
    and! emailAddress = this.EmailAddress |> EmailAddress.create
    and! name = this.Name |> NonEmptyString.createMaybe (Some "Name")
    return
      { Id = subscriptionId
        CreatedDate = createdDate
        EmailAddress = emailAddress
        Name = name
        IsActive = this.IsActive }
  }

/// <summary>
/// Database representation of a delivery to a subscription
/// </summary>
/// <remarks>
/// The <c>SubscriptionDelivery</c> entity has the following structure: <br/>
/// <c>
/// {
///    "PartitionKey": "&lt;subscriptionId&gt;",
///    "RowKey": "&lt;deliveryId&gt;",
///    "Timestamp": "&lt;date&gt;",
///    "CreatedDate": "&lt;date&gt;",
///    "Type": "&lt;welcome | newpost&gt;",
///    "Post": "&lt;slug&gt;",
///    "OperationId": "&lt;string&gt;"
///  }
/// </c>
/// </remarks>
type internal DbSubscriptionDelivery =
  { SubscriptionId : string
    DeliveryId : string
    CreatedDate : DateTime
    Type : string
    Post : string
    OperationId : string }

  static member FromEntity (entity : TableEntity) = validation {
    let entityName = nameof DbSubscriptionDelivery
    let! createdDate =
      "CreatedDate"
      |> entity.RequireString entityName
      |> Validation.bind DateTime.TryValidateUtc
    and! ``type`` = "Type" |> entity.RequireString entityName
    and! post = "Post" |> entity.RequireString entityName
    and! operationId = "OperationId" |> entity.RequireString entityName

    return
      { SubscriptionId = entity.PartitionKey
        DeliveryId = entity.RowKey
        CreatedDate = createdDate
        Type = ``type``
        Post = post
        OperationId = operationId }
  }

  member this.ToEntity() =
    let entity = TableEntity(partitionKey = this.SubscriptionId, rowKey = this.DeliveryId)
    entity["CreatedDate"] <- this.CreatedDate.ToString("o", CultureInfo.InvariantCulture)
    entity["Type"] <- this.Type
    entity["Post"] <- this.Post
    entity["OperationId"] <- this.OperationId
    entity

  static member TypeToString = function
    | Welcome -> "welcome"
    | NewPost -> "newpost"

  member this.ToSubscriptionDelivery () = validation {
    let! id = this.DeliveryId |> SubscriptionDeliveryId.create
    and! createdDate = DateTime.TryValidateUtc(
      this.CreatedDate.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
      "CreatedDate"
    )
    and! ``type`` =
      match this.Type with
      | "welcome" -> Validation.ok Welcome
      | "newpost" -> Validation.ok NewPost
      | other -> Validation.error $"'{other}' is an unknown delivery type"
    and! post = this.Post |> Slug.create
    return
      { Id = id
        CreatedDate = createdDate
        Type = ``type``
        Post = post }
  }

type internal ISubscriptionDb =
  abstract member GetAllSubscriptions: unit -> CancellableTaskResult<DbSubscription list, DependencyError>
  abstract member GetSubscriptionByEmail: email: string -> CancellableTaskResult<DbSubscription option, DependencyError>
  abstract member GetSubscriptionById: id: string -> CancellableTaskResult<DbSubscription option, DependencyError>
  abstract member CreateSubscription: subscription: DbSubscription -> CancellableTaskResult<DbSubscription, DependencyError>
  abstract member UpdateSubscriptionActivation: 
    subscription: DbSubscription
      -> isActive: bool
      -> CancellableTaskResult<DbSubscription, DependencyError>
  abstract member GetDeliveriesForSubscription:
    subscriptionId: string -> CancellableTaskResult<DbSubscriptionDelivery list, DependencyError>
  abstract member CreateDelivery:
    delivery: DbSubscriptionDelivery -> CancellableTaskResult<DbSubscriptionDelivery, DependencyError>

module internal SubscriptionDbError =
  let private subscriptionDbError =
    { Dependency = Dependency.Database
      Type = Unknown (exn "") }
    
  let validation msg = { subscriptionDbError with Type = DependencyErrorType.Validation msg }
    
  let unknown msg = { subscriptionDbError with Type = msg |> FSharp.Core.exn |> DependencyErrorType.Unknown }


module internal SubscriptionDb =
  
  open Azure.Data.Tables.FSharp
  open Microsoft.Extensions.Logging
  
  let live (tableServiceClient : TableServiceClient) (logger : ILogger) =

    let db = nameof ISubscriptionDb 

    let trySingle table onEntity partition key =
      Db.trySingle tableServiceClient logger db table onEntity partition key

    let trySingleQuery table onEntity partition query =
      Db.trySingleQuery tableServiceClient logger db table onEntity partition query
      
    let toList table onEntity =
      Db.toList tableServiceClient logger db table onEntity
      
    let add table onError onEntity entity =
      Db.add tableServiceClient logger db table onError onEntity entity
      
    let update table onError onEntity entity updateEntity =
      Db.update tableServiceClient logger db table onError onEntity entity updateEntity
      
    let where table onEntity filter =
      Db.where tableServiceClient logger db table onEntity filter

    let subscriptionsTable = "subscriptions"
    let deliveriesTable = "deliveries"

    { new ISubscriptionDb with
      
      member _.GetAllSubscriptions () = 
        toList subscriptionsTable DbSubscription.FromEntity
      
      member _.GetSubscriptionByEmail emailAddress =
        eq "EmailAddress" emailAddress |> trySingleQuery subscriptionsTable DbSubscription.FromEntity None

      member _.GetSubscriptionById id =
        id |> trySingle subscriptionsTable DbSubscription.FromEntity (Some id) 

      member _.CreateSubscription subscription =
        subscription.ToEntity() |> add subscriptionsTable SubscriptionDbError.validation DbSubscription.FromEntity

      member _.UpdateSubscriptionActivation subscription isActive =
        subscription.ToEntity() |> update subscriptionsTable SubscriptionDbError.validation DbSubscription.FromEntity
        <| fun entity ->
          entity["IsActive"] <- isActive
          entity

      member _.GetDeliveriesForSubscription subscriptionId =
        eq "PartitionKey" subscriptionId |> where deliveriesTable DbSubscriptionDelivery.FromEntity

      member _.CreateDelivery delivery =
        delivery.ToEntity() |> add deliveriesTable SubscriptionDbError.validation DbSubscriptionDelivery.FromEntity
    }
  
