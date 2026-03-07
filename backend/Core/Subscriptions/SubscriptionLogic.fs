namespace TwoPoint.Core.Subscriptions.Logic

open FsToolkit.ErrorHandling

open TwoPoint.Core.Posts
open TwoPoint.Core.Subscriptions
open TwoPoint.Core.Shared

// ========================================================================================
// Create subscription 
// ========================================================================================

// Inputs

type NewSubscription =
  { EmailAddress : EmailAddress
    Name : NonEmptyString option }

// Outputs

type CreateSubscriptionError = unit

type SubscriptionCreated = 
  | New of NewSubscription
  | Reactivate of Subscription

type CreateSubscriptionDecision = Decision<SubscriptionCreated, CreateSubscriptionError>

type CreateSubscription =
  Subscription option
    -> NewSubscription
    -> CreateSubscriptionDecision

// Logic

module CreateSubscription =

  let decide : CreateSubscription = 
    fun existingSubscription newSubscription ->
      existingSubscription
      |> Option.map Reactivate
      |> Option.defaultValue (New newSubscription)
      |> Ok

// ========================================================================================
// Cancel subscription 
// ========================================================================================

// Inputs

type SubscriptionCancellation =
  | ByEmailAddress of EmailAddress
  | BySubscriptionId of SubscriptionId

// Outputs

type CancelSubscriptionError = SubscriptionNotFound of SubscriptionCancellation 

type SubscriptionCancelled = SubscriptionCancelled of Subscription

type CancelSubscriptionDecision = Decision<SubscriptionCancelled, CancelSubscriptionError>

type CancelSubscription =
  Subscription option
    -> SubscriptionCancellation
    -> CancelSubscriptionDecision 

// Logic

module CancelSubscription =

  let decide : CancelSubscription = 
    fun existingSubscription cancellation ->
      existingSubscription 
      |> Result.requireSome (SubscriptionNotFound cancellation)
      |> Result.map SubscriptionCancelled
    

// ========================================================================================
// Send welcome 
// ========================================================================================

// Outputs

type SendWelcomeDependency =
  | Subscription of SubscriptionId
  | Post of Slug

type SendWelcomeError =
  | DependencyNotFound of SendWelcomeDependency
  | WelcomeAlreadySent of Subscription * SubscriptionDelivery 

type WelcomeSent = WelcomeSent of Subscription * Post

type SendWelcomeDecision = Decision<WelcomeSent, SendWelcomeError>

type SendWelcome =
  Subscription option
    -> Map<SubscriptionDeliveryType, SubscriptionDelivery list>
    -> Post
    -> SubscriptionId
    -> SendWelcomeDecision 

// Logic

module SendWelcome =

  let decide : SendWelcome = 
    fun subscription deliveries latestPost subscriptionId -> result {
      let! subscription = subscription |> Result.requireSome (DependencyNotFound (Subscription subscriptionId))
      let welcomeDeliveries = 
        deliveries 
        |> Map.tryFind Welcome
        |> Option.defaultValue []
      // Don't send multiple welcomes if the latest post hasn't changed.
      // This will ensure someone doesn't get multiple emails ex. subscribe -> unsubscribe -> subscribe
      // They *should* get another welcome email if they re-subscribe and there have since been new posts
      let existingWelcomeForSamePost = welcomeDeliveries |> List.tryFind (fun d -> d.Post = latestPost.Id)
      do! 
        existingWelcomeForSamePost 
        |> Result.requireNoneWith (fun welcome -> WelcomeAlreadySent (subscription, welcome))
      return WelcomeSent (subscription, latestPost)
    }