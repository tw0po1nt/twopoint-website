namespace TwoPoint.Core.Subscriptions.Actions

open TwoPoint.Core.Posts
open TwoPoint.Core.Subscriptions
open TwoPoint.Core.Shared
open TwoPoint.Core.Util

open FsToolkit.ErrorHandling
open IcedTasks

open System

// ========================================================================================
// Create subscription 
// ========================================================================================

// Input

type NewSubscription =
  { EmailAddress : EmailAddress
    Name : NonEmptyString option }

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

[<RequireQualifiedAccess>]
module CreateSubscription =

  // Output
  type Error = unit
  type Success = 
    | New of NewSubscription
    | Reactivate of Subscription
  type Decision = Decision<Success, Error>
  type Event = Created of Subscription

  // Logic

  type Logic =
    Subscription option
      -> NewSubscription
      -> Decision

  [<RequireQualifiedAccess>]
  module Logic =
    let decide : Logic = 
      fun existingSubscription newSubscription ->
        existingSubscription
        |> Option.map Reactivate
        |> Option.defaultValue (New newSubscription)
        |> Ok

  // Action

  type Action = NewSubscription.Dto -> CancellableTask<ActionResult<Event, Error>>

  type Dependencies =
    abstract member GetSubscriptionByEmail: EmailAddress -> DependencyResult<Subscription option>
    abstract member GetUtcNow: unit -> DateTime
    abstract member CreateSubscription: 
      newSubscription: NewSubscription 
        -> createdDateUtc: DateTime
        -> DependencyResult<Subscription>
    abstract member ActivateSubscription: Subscription -> DependencyResult<Subscription>

  let withDependencies (dependencies : Dependencies) : Action =
    fun newSubscription -> cancellableTaskResult {
      let! newSubscription = 
        newSubscription 
        |> NewSubscription.create
        |> Result.mapError ActionError<Error>.Validation

      let! existingSubscription =
        dependencies.GetSubscriptionByEmail newSubscription.EmailAddress
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let utcNow = dependencies.GetUtcNow()

      let decision = Logic.decide existingSubscription newSubscription

      return!
        decision
        |> Result.traverseCancellableTask (function
          | New newSubscription -> dependencies.CreateSubscription newSubscription utcNow
          | Reactivate existing -> dependencies.ActivateSubscription existing
        )
        |> CancellableTaskResult.foldResult
          (DependencyResult.toActionResult Event.Created)
          (Logic >> ActionResult.failure)
    }

// ========================================================================================
// Cancel subscription 
// ========================================================================================

// Input

type SubscriptionCancellation =
  | ByEmailAddress of EmailAddress
  | BySubscriptionId of SubscriptionId

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

[<RequireQualifiedAccess>]
module CancelSubscription =

  // Output

  type Error = SubscriptionNotFound of SubscriptionCancellation 
  type Success = Cancelled of Subscription
  type Decision = Decision<Success, Error>
  type Event = Cancelled of Subscription
  
  // Logic

  type Logic =
    Subscription option
      -> SubscriptionCancellation
      -> Decision 

  [<RequireQualifiedAccess>]
  module Logic =
    let decide : Logic = 
      fun existingSubscription cancellation ->
        existingSubscription 
        |> Result.requireSome (SubscriptionNotFound cancellation)
        |> Result.map Success.Cancelled
   
  // Action

  type Action = SubscriptionCancellation.Dto -> CancellableTask<ActionResult<Event, Error>>

  type Dependencies =
    abstract member GetSubscriptionByEmail: EmailAddress -> DependencyResult<Subscription option>
    abstract member GetSubscriptionById: SubscriptionId -> DependencyResult<Subscription option>
    abstract member DeactivateSubscription: Subscription -> DependencyResult<Subscription>

  let withDependencies (dependencies : Dependencies) : Action =
    fun cancellation -> cancellableTaskResult {
      let! cancellation = 
        cancellation 
        |> SubscriptionCancellation.create
        |> Result.mapError ActionError<Error>.Validation

      let! existingSubscription =
        match cancellation with
        | ByEmailAddress emailAddress     -> dependencies.GetSubscriptionByEmail emailAddress
        | BySubscriptionId subscriptionId -> dependencies.GetSubscriptionById subscriptionId
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let decision = Logic.decide existingSubscription cancellation

      return!
        decision
        |> Result.map (fun (Success.Cancelled subscription) -> subscription)
        |> Result.traverseCancellableTask dependencies.DeactivateSubscription 
        |> CancellableTaskResult.foldResult
          (DependencyResult.toActionResult Event.Cancelled)
          (Logic >> ActionResult.failure)
    }

// ========================================================================================
// Send welcome 
// ========================================================================================

// Input

type UnvalidatedSubscriptionId = UnvalidatedSubscriptionId of string

[<RequireQualifiedAccess>]
module SendWelcome =
  
  // Output

  type Error =
    | NotFound of Data
    | WelcomeAlreadySent of Subscription * SubscriptionDelivery 
  and Data =
    | Subscription of SubscriptionId
    | Post of Slug
  type Success = Sent of Subscription * Post
  type Decision = Decision<Success, Error>
  type Event = Sent of Subscription * SubscriptionDelivery
  
  // Logic

  type Logic =
    Subscription option
      -> Map<SubscriptionDeliveryType, SubscriptionDelivery list>
      -> Post
      -> SubscriptionId
      -> Decision 

  [<RequireQualifiedAccess>]
  module Logic =
    let decide : Logic = 
      fun subscription deliveries latestPost subscriptionId -> result {
        let! subscription = subscription |> Result.requireSome (subscriptionId |> Data.Subscription |> Error.NotFound)
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
        return Success.Sent (subscription, latestPost)
      }

  // Action

  type SendWelcome = UnvalidatedSubscriptionId -> CancellableTask<ActionResult<Event, Error>>

  type Dependencies =
    // todo: implement these next
    abstract member GetSubscriptionById: SubscriptionId -> DependencyResult<Subscription option>
    abstract member GetDeliveriesForSubscription: Subscription -> DependencyResult<Map<SubscriptionDeliveryType, SubscriptionDelivery list>>
    abstract member GetLatestPost: unit -> DependencyResult<Post>
    abstract member SendWelcome: Subscription -> Post -> DependencyResult<Subscription * SubscriptionDelivery>

  let withDependencies (dependencies : Dependencies) : SendWelcome =
    fun (UnvalidatedSubscriptionId subscriptionId) -> cancellableTaskResult {
      let! subscriptionId = 
        subscriptionId 
        |> SubscriptionId.create
        |> Result.mapError ActionError<Error>.Validation

      let! (subscription: Subscription option) =
        dependencies.GetSubscriptionById subscriptionId
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let! deliveries =
        subscription
        |> Option.traverseCancellableTaskResult dependencies.GetDeliveriesForSubscription
        |> CancellableTaskResult.map (Option.defaultValue Map.empty)
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let! (latestPost : Post) =
        dependencies.GetLatestPost()
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let decision = Logic.decide subscription deliveries latestPost subscriptionId

      return!
        decision
        |> Result.map (fun (Success.Sent (subscription, post)) -> subscription, post)
        |> Result.traverseCancellableTask (uncurry dependencies.SendWelcome)
        |> CancellableTaskResult.foldResult
          (DependencyResult.toActionResult Event.Sent)
          (Logic >> ActionResult.failure)
    }
