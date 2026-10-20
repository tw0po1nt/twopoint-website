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


module internal NewsletterPreparation =

  let prepare notFound invalid (post : Post) (html : string option) =
    html
    |> Result.requireSome (notFound post)
    |> Result.bind (Newsletter.create >> Result.mapError (fun reason -> invalid (post, reason)))

module internal IntroText =

  let create (text : string option) =
    text
    |> Option.map _.Trim()
    |> Option.bind (NonEmptyString.create None >> Result.toOption)

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
      NewsletterPreparation.prepare NewsletterNotFound NewsletterInvalid post html

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


// ========================================================================================
// Start new-post delivery
// ========================================================================================

// Input

type NewPostDeliveryStart =
  { Post : Slug
    Intro : NonEmptyString option }

module NewPostDeliveryStart =

  type Post = Post of string
  type Intro = Intro of string

  type Dto =
    { Post : Post
      Intro : Intro option }

  let create (dto : Dto) = validation {
    let (Post post) = dto.Post

    let! post = post |> Slug.create

    return
      { NewPostDeliveryStart.Post = post
        Intro = dto.Intro |> Option.map (fun (Intro intro) -> intro) |> IntroText.create }
  }

[<RequireQualifiedAccess>]
module StartNewPostDelivery =

  // Output

  type Error =
    | PostNotFound of Slug
    | PostUpcoming of Post
    | NewsletterNotFound of Post
    | NewsletterInvalid of Post * reason : string
  type Success = Start of Post
  type Decision = Decision<Success, Error>
  type Event = Started of Post * recipients : Subscription list

  // Logic

  type Logic =
    DateTime
      -> Slug
      -> Post option
      -> Decision

  [<RequireQualifiedAccess>]
  module Logic =
    let decide : Logic =
      fun utcNow slug post -> result {
        let! post = post |> Result.requireSome (PostNotFound slug)
        do! post.Info.CreatedDate <= utcNow |> Result.requireTrue (PostUpcoming post)
        return Start post
      }

    let prepareNewsletter (post : Post) (html : string option) : Result<Newsletter, Error> =
      NewsletterPreparation.prepare NewsletterNotFound NewsletterInvalid post html

    let recipients (subscriptions : Subscription list) = subscriptions |> List.filter _.IsActive

  // Action

  type Action = NewPostDeliveryStart.Dto -> CancellableTask<ActionResult<Event, Error>>

  type Dependencies =
    abstract member GetPostBySlug: Slug -> DependencyResult<Post option>
    abstract member GetNewsletter: Post -> DependencyResult<string option>
    abstract member GetAllSubscriptions: unit -> DependencyResult<Subscription list>
    abstract member GetUtcNow: unit -> DateTime
    abstract member QueueDeliveries:
      post: Post
        -> intro: NonEmptyString option
        -> recipients: Subscription list
        -> DependencyResult<unit>

  let withDependencies (dependencies : Dependencies) : Action =
    fun start -> cancellableTaskResult {
      let! start =
        start
        |> NewPostDeliveryStart.create
        |> Result.mapError ActionError<Error>.Validation

      let! post =
        dependencies.GetPostBySlug start.Post
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let! (Start post) =
        Logic.decide (dependencies.GetUtcNow()) start.Post post
        |> Result.mapError ActionError<Error>.Logic

      let! html =
        dependencies.GetNewsletter post
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      do!
        Logic.prepareNewsletter post html
        |> Result.ignore
        |> Result.mapError ActionError<Error>.Logic

      let! subscriptions =
        dependencies.GetAllSubscriptions()
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let recipients = Logic.recipients subscriptions

      do!
        dependencies.QueueDeliveries post start.Intro recipients
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      return Event.Started (post, recipients)
    }


// ========================================================================================
// Send new-post delivery
// ========================================================================================

// Input

type NewPostDeliveryRequest =
  { SubscriptionId : SubscriptionId
    Post : Slug
    Intro : NonEmptyString option }

module NewPostDeliveryRequest =

  type SubscriptionId = SubscriptionId of string
  type Post = Post of string
  type Intro = Intro of string

  type Dto =
    { SubscriptionId : SubscriptionId
      Post : Post
      Intro : Intro option }

  let create (dto : Dto) = validation {
    let (SubscriptionId subscriptionId) = dto.SubscriptionId
    let (Post post) = dto.Post

    let! subscriptionId = subscriptionId |> SubscriptionId.create
    and! post = post |> Slug.create

    return
      { NewPostDeliveryRequest.SubscriptionId = subscriptionId
        Post = post
        Intro = dto.Intro |> Option.map (fun (Intro intro) -> intro) |> IntroText.create }
  }

[<RequireQualifiedAccess>]
module SendNewPostDelivery =

  // Output

  type Error =
    | SubscriptionNotFound of SubscriptionId
    | SubscriptionInactive of Subscription
    | PostNotFound of Slug
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
      -> Slug
      -> Post option
      -> Decision

  [<RequireQualifiedAccess>]
  module Logic =
    let decide : Logic =
      fun subscriptionId subscription deliveries slug post -> result {
        let! subscription = subscription |> Result.requireSome (SubscriptionNotFound subscriptionId)
        do! subscription.IsActive |> Result.requireTrue (SubscriptionInactive subscription)
        let! post = post |> Result.requireSome (PostNotFound slug)
        // A delivery of any type counts, so a subscription that got the post in its welcome does not get it again.
        do!
          deliveries
          |> List.tryFind (fun delivery -> delivery.Post = post.Id)
          |> Result.requireNoneWith (fun delivery -> AlreadySent (subscription, delivery))
        return Send (subscription, post)
      }

    let prepareNewsletter (post : Post) (html : string option) : Result<Newsletter, Error> =
      NewsletterPreparation.prepare NewsletterNotFound NewsletterInvalid post html

  // Action

  type Action = NewPostDeliveryRequest.Dto -> CancellableTask<ActionResult<Event, Error>>

  type Dependencies =
    abstract member GetSubscriptionById: SubscriptionId -> DependencyResult<Subscription option>
    abstract member GetDeliveriesForSubscription: Subscription -> DependencyResult<SubscriptionDelivery list>
    abstract member GetPostBySlug: Slug -> DependencyResult<Post option>
    abstract member GetNewsletter: Post -> DependencyResult<string option>
    abstract member GetUtcNow: unit -> DateTime
    abstract member SendNewPost:
      subscription: Subscription
        -> post: Post
        -> intro: NonEmptyString option
        -> newsletter: Newsletter
        -> DependencyResult<OperationId>
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
        |> NewPostDeliveryRequest.create
        |> Result.mapError ActionError<Error>.Validation

      let! subscription =
        dependencies.GetSubscriptionById request.SubscriptionId
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let! deliveries =
        subscription
        |> Option.traverseCancellableTaskResult dependencies.GetDeliveriesForSubscription
        |> CancellableTaskResult.map (Option.defaultValue [])
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let! post =
        dependencies.GetPostBySlug request.Post
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let! (Send (subscription, post)) =
        Logic.decide request.SubscriptionId subscription deliveries request.Post post
        |> Result.mapError ActionError<Error>.Logic

      let! html =
        dependencies.GetNewsletter post
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let! newsletter =
        Logic.prepareNewsletter post html
        |> Result.mapError ActionError<Error>.Logic

      let! operationId =
        dependencies.SendNewPost subscription post request.Intro newsletter
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let! delivery =
        dependencies.CreateDelivery subscription NewPost post operationId (dependencies.GetUtcNow())
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      return Event.Sent (subscription, delivery)
    }
