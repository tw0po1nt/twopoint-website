namespace TwoPoint.Core.Subscriptions

open TwoPoint.Core.Shared
open TwoPoint.Core.Posts

open System

type SubscriptionId =
  private | SubscriptionId of Guid

  override this.ToString () =
    let (SubscriptionId sid) = this
    sid.ToString()

module SubscriptionId =
  let create =
    Constrained.String.tryParse
      Guid.TryParseOption
      $"'{nameof SubscriptionId}' must be a valid guid"
      SubscriptionId

  let createMaybe = Types.createMaybe create

  let ofGuid (guid : Guid) = SubscriptionId guid

  let value (SubscriptionId sid) = sid


type SubscriptionDeliveryId =
  private | SubscriptionDeliveryId of Guid

  override this.ToString () =
    let (SubscriptionDeliveryId sdid) = this
    sdid.ToString()

module SubscriptionDeliveryId =
  let create =
    Constrained.String.tryParse
      Guid.TryParseOption
      $"'{nameof SubscriptionDeliveryId}' must be a valid guid"
      SubscriptionDeliveryId

  let createMaybe = Types.createMaybe create

  let value (SubscriptionDeliveryId sdid) = sdid


type SubscriptionDeliveryType =
  | Welcome
  | NewPost

type SubscriptionDelivery =
  { Id : SubscriptionDeliveryId
    CreatedDate : DateTime
    Type : SubscriptionDeliveryType
    Post : Slug }

/// `Pending` gets no post until the subscriber confirms with the link of the confirmation email.
type SubscriptionStatus =
  | Pending of requestedDate : DateTime
  | Active
  | Canceled of DateTime

type Subscription =
  { Id : SubscriptionId
    CreatedDate : DateTime
    EmailAddress : EmailAddress
    Name : NonEmptyString option
    Status : SubscriptionStatus }

module Subscription =

  let cancel (canceledDateUtc : DateTime) (subscription : Subscription) =
    { subscription with Status = Canceled canceledDateUtc }

  let request (requestedDate : DateTime) (subscription : Subscription) =
    { subscription with Status = Pending requestedDate }

  let confirm (subscription : Subscription) =
    { subscription with Status = Active }

  let isActive (subscription : Subscription) =
    match subscription.Status with
    | Active -> true
    | Pending _
    | Canceled _ -> false

  let isPending (subscription : Subscription) =
    match subscription.Status with
    | Pending _ -> true
    | Active
    | Canceled _ -> false

type StoredSubscription = { Subscription : Subscription; ETag : string }

type DeliveredPost =
  { Delivery : SubscriptionDelivery
    PostTitle : NonEmptyString option }
