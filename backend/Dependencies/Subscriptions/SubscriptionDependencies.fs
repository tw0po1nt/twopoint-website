namespace TwoPoint.Dependencies.Subscriptions

open TwoPoint.Core.Subscriptions
open TwoPoint.Core.Subscriptions.Logic
open TwoPoint.Core.Shared
open TwoPoint.Core.Util

open System

type ISubscriptionDependencies =
  // todo
  // abstract member GetActiveSubscriptions: unit -> DependencyResult<Subscription list>
  abstract member GetSubscriptionByEmail: email: EmailAddress -> DependencyResult<Subscription option>
  abstract member GetSubscriptionById: id: SubscriptionId -> DependencyResult<Subscription option>
  abstract member CreateSubscription: 
    newSubscription: NewSubscription
      -> createdDateUtc: DateTime
      -> DependencyResult<Subscription>
  abstract member UpdateSubscriptionActivation: 
    subscription: Subscription
      -> isActive: bool
      -> DependencyResult<Subscription>
      
[<RequireQualifiedAccess>]
module SubscriptionDependencies =
  
  open FsToolkit.ErrorHandling
  
  let live tableServiceClient logger =
    let subscriptionDb = SubscriptionDb.live tableServiceClient logger

    let source = Dependency.Database
    
    { new ISubscriptionDependencies with
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
      
      member _.CreateSubscription newSubscription createdDateUtc = cancellableTaskResult {
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
    }



