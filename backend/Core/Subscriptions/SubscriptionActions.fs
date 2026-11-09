namespace TwoPoint.Core.Subscriptions.Actions

open TwoPoint.Core.Deploys
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
  type Error =
    | AlreadyActive of Subscription
    | ConfirmationRecent of Subscription
  type Success =
    | New of NewSubscription
    | Request of Subscription
  type Decision = Decision<Success, Error>
  type Event =
    | Created of Subscription
    | Requested of Subscription

  // Logic

  type Logic =
    DateTime
      -> Subscription option
      -> NewSubscription
      -> Decision

  [<RequireQualifiedAccess>]
  module Logic =
    /// The form takes any address, so this limit keeps a script from sending a stream of confirmation emails to one address.
    let resendAfter = TimeSpan.FromHours 1.0

    let decide : Logic =
      fun utcNow existingSubscription newSubscription ->
        match existingSubscription with
        | None -> Ok (New newSubscription)
        | Some ({ Status = Active } as existing) -> Error (AlreadyActive existing)
        | Some ({ Status = Pending requestedDate } as existing) when utcNow - requestedDate < resendAfter ->
          Error (ConfirmationRecent existing)
        | Some ({ Status = (Pending _ | Canceled _) } as existing) -> Ok (Request existing)

  // Action

  type Action = NewSubscription.Dto -> CancellableTask<ActionResult<Event, Error>>

  type Dependencies =
    abstract member GetSubscriptionByEmail: EmailAddress -> DependencyResult<Subscription option>
    abstract member GetUtcNow: unit -> DateTime
    abstract member NewSubscriptionId: unit -> SubscriptionId
    abstract member SendConfirmation: Subscription -> DependencyResult<unit>
    abstract member CreateSubscription: Subscription -> DependencyResult<Subscription>
    abstract member UpdateSubscriptionStatus: Subscription -> DependencyResult<Subscription>

  let withDependencies (dependencies : Dependencies) : Action =

    // The row goes after the send. A failed send then leaves no request date, so the subscriber can try again at once.
    let handle (utcNow : DateTime) (success : Success) : DependencyResult<Event> =
      cancellableTaskResult {
        match success with
        | New newSubscription ->
          let subscription =
            { Id = dependencies.NewSubscriptionId()
              CreatedDate = utcNow
              EmailAddress = newSubscription.EmailAddress
              Name = newSubscription.Name
              Status = Pending utcNow }
          do! dependencies.SendConfirmation subscription
          let! created = dependencies.CreateSubscription subscription
          return Event.Created created
        | Request existing ->
          let requested = Subscription.request utcNow existing
          do! dependencies.SendConfirmation requested
          let! updated = dependencies.UpdateSubscriptionStatus requested
          return Event.Requested updated
      }

    fun newSubscription -> cancellableTaskResult {
      let! newSubscription =
        newSubscription
        |> NewSubscription.create
        |> Result.mapError ActionError<Error>.Validation

      let! existingSubscription =
        dependencies.GetSubscriptionByEmail newSubscription.EmailAddress
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let utcNow = dependencies.GetUtcNow()

      return!
        Logic.decide utcNow existingSubscription newSubscription
        |> Result.traverseCancellableTask (handle utcNow)
        |> CancellableTaskResult.foldResult
          (DependencyResult.toActionResult id)
          (Logic >> ActionResult.failure)
    }

// ========================================================================================
// Confirm subscription
// ========================================================================================

/// The answer to the confirm page. `Unknown` is also the answer for a canceled subscription, because its link
/// must not start the emails again.
[<RequireQualifiedAccess>]
type SubscriptionConfirmState =
  | Confirmed
  | Unknown

[<RequireQualifiedAccess>]
type SubscriptionConfirmDecision =
  | Activate of Subscription
  | NoWrite of SubscriptionConfirmState

[<RequireQualifiedAccess>]
module SubscriptionConfirmRule =

  let confirm (found : Subscription option) : SubscriptionConfirmDecision =
    match found with
    | Some ({ Status = Pending _ } as pending) -> SubscriptionConfirmDecision.Activate (Subscription.confirm pending)
    | Some { Status = Active } -> SubscriptionConfirmDecision.NoWrite SubscriptionConfirmState.Confirmed
    | Some { Status = Canceled _ }
    | None -> SubscriptionConfirmDecision.NoWrite SubscriptionConfirmState.Unknown

[<RequireQualifiedAccess>]
module ConfirmSubscription =

  /// `Activated` is the only event that makes the subscription active, so only it gets the welcome.
  type Event =
    | Activated of Subscription
    | NoChange of SubscriptionConfirmState

  type Action = string -> CancellableTask<Result<Event, DependencyError>>

  type Dependencies =
    abstract member GetSubscriptionById: SubscriptionId -> DependencyResult<Subscription option>
    abstract member UpdateSubscriptionStatus: Subscription -> DependencyResult<Subscription>

  module Event =
    let state (event : Event) =
      match event with
      | Activated _ -> SubscriptionConfirmState.Confirmed
      | NoChange state -> state

  /// The text of the ID comes from the confirm link, so an ID that is not a GUID finds nothing.
  let withDependencies (dependencies : Dependencies) : Action =
    fun text -> cancellableTaskResult {
      let! found =
        match SubscriptionId.create text with
        | Error _ -> CancellableTaskResult.singleton None
        | Ok subscriptionId -> dependencies.GetSubscriptionById subscriptionId

      match SubscriptionConfirmRule.confirm found with
      | SubscriptionConfirmDecision.Activate confirmed ->
        let! updated = dependencies.UpdateSubscriptionStatus confirmed
        return Activated updated
      | SubscriptionConfirmDecision.NoWrite state -> return NoChange state
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
  type Success =
    | Cancelled of Subscription
    | AlreadyCanceled of Subscription
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
        |> Result.map (fun subscription ->
          match subscription.Status with
          | Active
          | Pending _ -> Success.Cancelled subscription
          | Canceled _ -> Success.AlreadyCanceled subscription)
   
  // Action

  type Action = SubscriptionCancellation.Dto -> CancellableTask<ActionResult<Event, Error>>

  type Dependencies =
    abstract member GetSubscriptionByEmail: EmailAddress -> DependencyResult<Subscription option>
    abstract member GetSubscriptionById: SubscriptionId -> DependencyResult<Subscription option>
    abstract member GetUtcNow: unit -> DateTime
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
        |> Result.traverseCancellableTask (function
          | Success.Cancelled subscription ->
            dependencies.DeactivateSubscription (Subscription.cancel (dependencies.GetUtcNow()) subscription)
          | Success.AlreadyCanceled subscription -> CancellableTaskResult.singleton subscription
        )
        |> CancellableTaskResult.foldResult
          (DependencyResult.toActionResult Event.Cancelled)
          (Logic >> ActionResult.failure)
    }

// ========================================================================================
// Delete subscription
// ========================================================================================

// Input

type SubscriptionDeletion =
  { SubscriptionId : SubscriptionId }

module SubscriptionDeletion =

  type SubscriptionId = SubscriptionId of string

  type Dto =
    { SubscriptionId : SubscriptionId }

  let create (dto : Dto) = validation {
    let (SubscriptionId subscriptionId) = dto.SubscriptionId

    let! subscriptionId = subscriptionId |> SubscriptionId.create

    return { SubscriptionDeletion.SubscriptionId = subscriptionId }
  }

[<RequireQualifiedAccess>]
module DeleteSubscription =

  // Output

  type Error = SubscriptionNotFound of SubscriptionId
  type Success = Deleted of Subscription * deliveries : SubscriptionDelivery list
  type Decision = Decision<Success, Error>
  type Event = Deleted of Subscription

  // Logic

  type Logic =
    SubscriptionDeletion
      -> Subscription option
      -> SubscriptionDelivery list
      -> Decision

  [<RequireQualifiedAccess>]
  module Logic =
    let decide : Logic =
      fun deletion existingSubscription deliveries -> result {
        let! subscription = existingSubscription |> Result.requireSome (SubscriptionNotFound deletion.SubscriptionId)
        return Success.Deleted (subscription, deliveries)
      }

  // Action

  type Action = SubscriptionDeletion.Dto -> CancellableTask<ActionResult<Event, Error>>

  type Dependencies =
    abstract member GetSubscriptionById: SubscriptionId -> DependencyResult<Subscription option>
    abstract member GetDeliveriesForSubscription: Subscription -> DependencyResult<SubscriptionDelivery list>
    abstract member DeleteDelivery: Subscription -> SubscriptionDelivery -> DependencyResult<unit>
    abstract member DeleteSubscription: Subscription -> DependencyResult<unit>

  let withDependencies (dependencies : Dependencies) : Action =

    // The subscription goes after its deliveries, so that a second try after a failed delete finds the deliveries that are left.
    let handleDeleted (Success.Deleted (subscription, deliveries)) : DependencyResult<Subscription> =
      cancellableTaskResult {
        for delivery in deliveries do
          do! dependencies.DeleteDelivery subscription delivery
        do! dependencies.DeleteSubscription subscription
        return subscription
      }

    fun deletion -> cancellableTaskResult {
      let! deletion =
        deletion
        |> SubscriptionDeletion.create
        |> Result.mapError ActionError<Error>.Validation

      let! existingSubscription =
        dependencies.GetSubscriptionById deletion.SubscriptionId
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let! deliveries =
        existingSubscription
        |> Option.traverseCancellableTaskResult dependencies.GetDeliveriesForSubscription
        |> CancellableTaskResult.map (Option.defaultValue [])
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      return!
        Logic.decide deletion existingSubscription deliveries
        |> Result.traverseCancellableTask handleDeleted
        |> CancellableTaskResult.foldResult
          (DependencyResult.toActionResult Event.Deleted)
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
        do! Subscription.isActive subscription |> Result.requireTrue (SubscriptionInactive subscription)
        let! latestPost = latestPost |> Result.requireSome NoPosts
        // A reactivation gets a second welcome only when a newer post exists than the one in the last welcome.
        do!
          deliveries
          |> List.tryFind (fun delivery -> delivery.Type = Welcome && delivery.Post = latestPost.Id)
          |> Result.requireNoneWith (fun welcome -> AlreadySent (subscription, welcome))
        return Send (subscription, latestPost)
      }

    let latestPost (utcNow : DateTime) (deploys : DeployContext) (posts : Post list) : Post option =
      posts
      |> List.filter (DeliveryGate.checkPost utcNow deploys >> Result.isOk)
      |> List.sortByDescending _.Info.CreatedDate
      |> List.tryHead

    let prepareNewsletter (post : Post) (html : string option) : Result<Newsletter, Error> =
      NewsletterPreparation.prepare NewsletterNotFound NewsletterInvalid post html

  // Action

  type Action = WelcomeRequest.Dto -> CancellableTask<ActionResult<Event, Error>>

  type Dependencies =
    abstract member GetSubscriptionById: SubscriptionId -> DependencyResult<Subscription option>
    abstract member GetDeliveriesForSubscription: Subscription -> DependencyResult<SubscriptionDelivery list>
    abstract member GetAllPosts: unit -> DependencyResult<Post list>
    abstract member GetDeployContext: unit -> DependencyResult<DeployContext>
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

      let! posts, deploys =
        CancellableTaskResult.parallelZip (dependencies.GetAllPosts()) (dependencies.GetDeployContext())
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let latestPost = Logic.latestPost (dependencies.GetUtcNow()) deploys posts

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
    | NotReady of Post * DeliveryGate.Refusal
    | AlreadyStarted of Post * startedDate : DateTime
    | NewsletterNotFound of Post
    | NewsletterInvalid of Post * reason : string
  type Success = Start of Post
  type Decision = Decision<Success, Error>
  type Event = Started of Post * recipients : Subscription list * startedDate : DateTime

  // Logic

  type Logic =
    DateTime
      -> Slug
      -> Post option
      -> DeployContext
      -> Decision

  [<RequireQualifiedAccess>]
  module Logic =
    let decide : Logic =
      fun utcNow slug post deploys -> result {
        let! post = post |> Result.requireSome (PostNotFound slug)
        do! DeliveryGate.checkPost utcNow deploys post |> Result.mapError (fun refusal -> NotReady (post, refusal))
        return Start post
      }

    let inserted (post : Post) (insert : DeliveryStartInsert) : Result<unit, Error> =
      match insert with
      | Inserted -> Ok ()
      | StartedBefore startedDate -> Error (AlreadyStarted (post, startedDate))

    let prepareNewsletter (post : Post) (html : string option) : Result<Newsletter, Error> =
      NewsletterPreparation.prepare NewsletterNotFound NewsletterInvalid post html

    let recipients (subscriptions : Subscription list) = subscriptions |> List.filter Subscription.isActive

  // Action

  type Action = NewPostDeliveryStart.Dto -> CancellableTask<ActionResult<Event, Error>>

  type Dependencies =
    abstract member GetPostBySlug: Slug -> DependencyResult<Post option>
    abstract member GetDeployContext: unit -> DependencyResult<DeployContext>
    abstract member GetNewsletter: Post -> DependencyResult<string option>
    abstract member GetAllSubscriptions: unit -> DependencyResult<Subscription list>
    abstract member GetUtcNow: unit -> DateTime
    abstract member InsertDeliveryStart: post: Post -> startedDate: DateTime -> DependencyResult<DeliveryStartInsert>
    abstract member DeleteDeliveryStart: post: Post -> DependencyResult<unit>
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

      let! deploys =
        dependencies.GetDeployContext()
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let! (Start post) =
        Logic.decide (dependencies.GetUtcNow()) start.Post post deploys
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
      let startedDate = dependencies.GetUtcNow()

      // The insert fails when another start inserted first, so two starts never queue the same post.
      let! insert =
        dependencies.InsertDeliveryStart post startedDate
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      do! Logic.inserted post insert |> Result.mapError ActionError<Error>.Logic

      // A failed queue deletes the start, so the author can start the delivery again.
      do!
        dependencies.QueueDeliveries post start.Intro recipients
        |> CancellableTaskResult.orElseWith (fun queueError ->
          dependencies.DeleteDeliveryStart post |> CancellableTask.map (fun _ -> Error queueError))
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      return Event.Started (post, recipients, startedDate)
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
        do! Subscription.isActive subscription |> Result.requireTrue (SubscriptionInactive subscription)
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
