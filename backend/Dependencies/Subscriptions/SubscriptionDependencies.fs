namespace TwoPoint.Dependencies.Subscriptions

open TwoPoint.Core.Posts
open TwoPoint.Core.Subscriptions
open TwoPoint.Core.Shared
open TwoPoint.Core.Subscriptions.Actions
open TwoPoint.Core.Util

open Azure.Data.Tables
open FsToolkit.ErrorHandling
open Microsoft.Extensions.Logging

open System
      
type SubscriptionDependencies(
  tableServiceClient : TableServiceClient,
  logger : ILogger
) =
 
 
  let subscriptionDb = SubscriptionDb.live tableServiceClient logger

  let source = Dependency.Database

  // todo
  // member _.GetActiveSubscriptions () = cancellableTaskResult {
  //   let! (dbSubscriptions: DbSubscription list) = subscriptionDb.GetAllSubscriptions()

  //   return!
  //     dbSubscriptions
  //     |> List.traverseValidationA _.ToSubscription()
  //     |> DependencyError.ofValidation source
  // }

  member _.GetSubscriptionByEmail emailAddress = cancellableTaskResult {
    let! dbSubscription = subscriptionDb.GetSubscriptionByEmail (emailAddress |> EmailAddress.value)

    return!
      dbSubscription 
      |> Option.traverseResult (fun (dbo: DbSubscription) -> dbo.ToSubscription())
      |> DependencyError.ofValidation source
  }

  member _.GetSubscriptionById id = cancellableTaskResult {
    let! dbSubscription = subscriptionDb.GetSubscriptionById (id.ToString())

    return!
      dbSubscription 
      |> Option.traverseResult (fun (dbo: DbSubscription) -> dbo.ToSubscription())
      |> DependencyError.ofValidation source
  }

  member _.CreateSubscription (newSubscription : NewSubscription) createdDateUtc = cancellableTaskResult {
    let newSubscription =
      { DbSubscription.SubscriptionId = Guid.NewGuid().ToString()
        CreatedDate = createdDateUtc 
        EmailAddress = newSubscription.EmailAddress |> EmailAddress.value
        Name = newSubscription.Name |> Option.map NonEmptyString.value
        IsActive = true }
    let! (dbSubscription: DbSubscription) = subscriptionDb.CreateSubscription newSubscription
    return! dbSubscription.ToSubscription() |> DependencyError.ofValidation source
  }

  member _.UpdateSubscriptionActivation subscription isActive = cancellableTaskResult {
    let dbSubscription =
      { DbSubscription.SubscriptionId = subscription.Id.ToString()
        CreatedDate = subscription.CreatedDate
        EmailAddress = subscription.EmailAddress |> EmailAddress.value
        Name = subscription.Name |> Option.map NonEmptyString.value
        IsActive = subscription.IsActive }

    let! (dbSubscription: DbSubscription) = subscriptionDb.UpdateSubscriptionActivation dbSubscription isActive
    return! dbSubscription.ToSubscription() |> DependencyError.ofValidation source
  }

  member _.GetDeliveriesForSubscription (subscription : Subscription) = cancellableTaskResult {
    let! dbDeliveries = subscriptionDb.GetDeliveriesForSubscription (subscription.Id.ToString())

    return!
      dbDeliveries
      |> List.traverseValidationA (fun (dbo: DbSubscriptionDelivery) -> dbo.ToSubscriptionDelivery())
      |> DependencyError.ofValidation source
  }

  member _.CreateDelivery (subscription : Subscription) deliveryType (post : Post) operationId createdDateUtc = cancellableTaskResult {
    let newDelivery =
      { DbSubscriptionDelivery.SubscriptionId = subscription.Id.ToString()
        DeliveryId = Guid.NewGuid().ToString()
        CreatedDate = createdDateUtc
        Type = DbSubscriptionDelivery.TypeToString deliveryType
        Post = post.Id |> Slug.value
        OperationId = operationId }
    let! (dbDelivery: DbSubscriptionDelivery) = subscriptionDb.CreateDelivery newDelivery
    return! dbDelivery.ToSubscriptionDelivery() |> DependencyError.ofValidation source
  }
