namespace TwoPoint.Core.Subscriptions.Api

open TwoPoint.Core.Subscriptions
open TwoPoint.Core.Subscriptions.Logic
open TwoPoint.Core.Shared
open TwoPoint.Core.Util

open FsToolkit.ErrorHandling
open IcedTasks

open System

// ========================================================================================
// Create subscription 
// ========================================================================================

// Input

module NewSubscription =

  type EmailAddress = EmailAddress of string
  type Name = Name of string

  type Dto =
    { EmailAddress : EmailAddress
      Name : Name option }

  let create (dto : Dto) = validation {
    let (EmailAddress emailAddress) = dto.EmailAddress
    let name = dto.Name |> Option.map (fun (Name name) -> name)

    let! emailAddress = emailAddress |> EmailAddress.create
    and! name = name |> NonEmptyString.createMaybe (Some (nameof name))

    return
      { NewSubscription.EmailAddress = emailAddress
        Name = name }
  }

// Output

type SubscriptionCreatedEvent = SubscriptionCreatedEvent of Subscription

// Action

type CreateSubscription = 
  NewSubscription.Dto 
    -> CancellableTask<ActionResult<SubscriptionCreatedEvent, CreateSubscriptionError>>

[<RequireQualifiedAccess>]
module CreateSubscription =

  type Dependencies =
    abstract member GetSubscriptionByEmail: EmailAddress -> DependencyResult<Subscription option>
    abstract member GetUtcNow: unit -> DateTime
    abstract member CreateSubscription: 
      newSubscription: NewSubscription 
        -> createdDateUtc: DateTime
        -> DependencyResult<Subscription>
    abstract member ActivateSubscription: Subscription -> DependencyResult<Subscription>

  let withDependencies (dependencies : Dependencies) : CreateSubscription =
    fun newSubscription -> cancellableTaskResult {
      let! newSubscription = 
        newSubscription 
        |> NewSubscription.create
        |> Result.mapError ActionError<CreateSubscriptionError>.Validation

      let! existingSubscription =
        dependencies.GetSubscriptionByEmail newSubscription.EmailAddress
        |> CancellableTaskResult.mapError ActionError<CreateSubscriptionError>.Dependency

      let utcNow = dependencies.GetUtcNow()

      let decision = CreateSubscription.decide existingSubscription newSubscription

      return!
        decision
        |> Result.traverseCancellableTask (function
          | New newSubscription -> dependencies.CreateSubscription newSubscription utcNow
          | Reactivate existing -> dependencies.ActivateSubscription existing
        )
        |> CancellableTaskResult.foldResult
          (DependencyResult.toActionResult SubscriptionCreatedEvent)
          (Logic >> ActionResult.failure)
    }

// ========================================================================================
// Cancel subscription 
// ========================================================================================

// Input

module SubscriptionCancellation =

  type EmailAddress = EmailAddress of string
  type SubscriptionId = SubscriptionId of string

  type Dto = 
    { EmailAddress : EmailAddress option
      SubscriptionId : SubscriptionId option }

  let create (dto : Dto) = validation {
    let! emailAddress =
      dto.EmailAddress
      |> Option.map (fun (EmailAddress ea) -> ea)
      |> Option.traverseValidation (EmailAddress.create >> Validation.map ByEmailAddress)
    let! subscriptionId =
      dto.SubscriptionId
      |> Option.map (fun (SubscriptionId id) -> id)
      |> Option.traverseValidation (SubscriptionId.create >> Validation.map BySubscriptionId)
    
    return!
      emailAddress
      |> Option.orElse subscriptionId
      |> Validation.requireSome $"'{nameof emailAddress}' or '{nameof subscriptionId}' is required"
  }
    

// Output

type SubscriptionCancelledEvent = SubscriptionCancelledEvent of Subscription

// Action

type CancelSubscription = 
  SubscriptionCancellation.Dto 
    -> CancellableTask<ActionResult<SubscriptionCancelledEvent, CancelSubscriptionError>>

[<RequireQualifiedAccess>]
module CancelSubscription =

  type Dependencies =
    abstract member GetSubscriptionByEmail: EmailAddress -> DependencyResult<Subscription option>
    abstract member GetSubscriptionById: SubscriptionId -> DependencyResult<Subscription option>
    abstract member DeactivateSubscription: Subscription -> DependencyResult<Subscription>

  let withDependencies (dependencies : Dependencies) : CancelSubscription =
    fun cancellation -> cancellableTaskResult {
      let! cancellation = 
        cancellation 
        |> SubscriptionCancellation.create
        |> Result.mapError ActionError<CancelSubscriptionError>.Validation

      let! existingSubscription =
        match cancellation with
        | ByEmailAddress emailAddress     -> dependencies.GetSubscriptionByEmail emailAddress
        | BySubscriptionId subscriptionId -> dependencies.GetSubscriptionById subscriptionId
        |> CancellableTaskResult.mapError ActionError<CancelSubscriptionError>.Dependency

      let decision = CancelSubscription.decide existingSubscription cancellation

      return!
        decision
        |> Result.map (fun (SubscriptionCancelled subscription) -> subscription)
        |> Result.traverseCancellableTask dependencies.DeactivateSubscription 
        |> CancellableTaskResult.foldResult
          (DependencyResult.toActionResult SubscriptionCancelledEvent)
          (Logic >> ActionResult.failure)
    }