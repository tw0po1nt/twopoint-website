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
  type Error = AlreadyActive of Subscription
  type Success =
    | New of NewSubscription
    | Reactivate of Subscription
  type Decision = Decision<Success, Error>
  type Event =
    | Created of Subscription
    | Reactivated of Subscription

  // Logic

  type Logic =
    Subscription option
      -> NewSubscription
      -> Decision

  [<RequireQualifiedAccess>]
  module Logic =
    let decide : Logic =
      fun existingSubscription newSubscription ->
        match existingSubscription with
        | None -> Ok (New newSubscription)
        | Some existing when existing.IsActive -> Error (AlreadyActive existing)
        | Some existing -> Ok (Reactivate existing)

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
          | New newSubscription ->
            dependencies.CreateSubscription newSubscription utcNow |> CancellableTaskResult.map Event.Created
          | Reactivate existing ->
            dependencies.ActivateSubscription existing |> CancellableTaskResult.map Event.Reactivated
        )
        |> CancellableTaskResult.foldResult
          (DependencyResult.toActionResult id)
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

type WelcomeRequest =
  { SubscriptionId : SubscriptionId }

module WelcomeRequest =

  type SubscriptionId = SubscriptionId of string

  type Dto =
    { SubscriptionId : SubscriptionId }

  let create (dto : Dto) = validation {
    let (SubscriptionId subscriptionId) = dto.SubscriptionId

    let! subscriptionId = subscriptionId |> SubscriptionId.create

    return { WelcomeRequest.SubscriptionId = subscriptionId }
  }

[<RequireQualifiedAccess>]
module SendWelcome =

  // Output

  type Error =
    | SubscriptionNotFound of SubscriptionId
    | SubscriptionInactive of Subscription
    | NoPosts
    | AlreadySent of Subscription * SubscriptionDelivery
    | NewsletterNotFound of Post
    | NewsletterInvalid of Post * reason : string
  type Success = Send of Subscription * Post
  type Decision = Decision<Success, Error>
  type Event = Sent of Subscription * SubscriptionDelivery

  // Logic

  type Logic =
    SubscriptionId
      -> Subscription option
      -> SubscriptionDelivery list
      -> Post option
      -> Decision

  [<RequireQualifiedAccess>]
  module Logic =
    let decide : Logic =
      fun subscriptionId subscription deliveries latestPost -> result {
        let! subscription = subscription |> Result.requireSome (SubscriptionNotFound subscriptionId)
        do! subscription.IsActive |> Result.requireTrue (SubscriptionInactive subscription)
        let! latestPost = latestPost |> Result.requireSome NoPosts
        // A reactivation gets a second welcome only when a newer post exists than the one in the last welcome.
        do!
          deliveries
          |> List.tryFind (fun delivery -> delivery.Type = Welcome && delivery.Post = latestPost.Id)
          |> Result.requireNoneWith (fun welcome -> AlreadySent (subscription, welcome))
        return Send (subscription, latestPost)
      }

    let prepareNewsletter (post : Post) (html : string option) : Result<Newsletter, Error> =
      html
      |> Result.requireSome (NewsletterNotFound post)
      |> Result.bind (Newsletter.create >> Result.mapError (fun reason -> NewsletterInvalid (post, reason)))

  // Action

  type Action = WelcomeRequest.Dto -> CancellableTask<ActionResult<Event, Error>>

  type Dependencies =
    abstract member GetSubscriptionById: SubscriptionId -> DependencyResult<Subscription option>
    abstract member GetDeliveriesForSubscription: Subscription -> DependencyResult<SubscriptionDelivery list>
    abstract member GetLatestPost: unit -> DependencyResult<Post option>
    abstract member GetNewsletter: Post -> DependencyResult<string option>
    abstract member GetUtcNow: unit -> DateTime
    abstract member SendWelcome: Subscription -> Newsletter -> DependencyResult<OperationId>
    abstract member CreateDelivery:
      subscription: Subscription
        -> deliveryType: SubscriptionDeliveryType
        -> post: Post
        -> operationId: OperationId
        -> createdDateUtc: DateTime
        -> DependencyResult<SubscriptionDelivery>

  let withDependencies (dependencies : Dependencies) : Action =
    fun request -> cancellableTaskResult {
      let! request =
        request
        |> WelcomeRequest.create
        |> Result.mapError ActionError<Error>.Validation

      let! subscription =
        dependencies.GetSubscriptionById request.SubscriptionId
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let! deliveries =
        subscription
        |> Option.traverseCancellableTaskResult dependencies.GetDeliveriesForSubscription
        |> CancellableTaskResult.map (Option.defaultValue [])
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let! latestPost =
        dependencies.GetLatestPost()
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let! (Send (subscription, post)) =
        Logic.decide request.SubscriptionId subscription deliveries latestPost
        |> Result.mapError ActionError<Error>.Logic

      let! html =
        dependencies.GetNewsletter post
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let! newsletter =
        Logic.prepareNewsletter post html
        |> Result.mapError ActionError<Error>.Logic

      let! operationId =
        dependencies.SendWelcome subscription newsletter
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      // Record the delivery after the send. If the record fails, a retry can send a second welcome. This order
      // makes sure that each subscription gets its welcome.
      let! delivery =
        dependencies.CreateDelivery subscription Welcome post operationId (dependencies.GetUtcNow())
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      return Event.Sent (subscription, delivery)
    }
