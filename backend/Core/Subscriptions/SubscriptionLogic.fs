namespace TwoPoint.Core.Subscriptions.Logic

open FsToolkit.ErrorHandling

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
    
