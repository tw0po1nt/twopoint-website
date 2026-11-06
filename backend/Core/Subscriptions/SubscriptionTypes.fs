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

type SubscriptionStatus =
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

  let reactivate (subscription : Subscription) =
    { subscription with Status = Active }

  let isActive (subscription : Subscription) =
    match subscription.Status with
    | Active -> true
    | Canceled _ -> false

type DeliveredPost =
  { Delivery : SubscriptionDelivery
    PostTitle : NonEmptyString option }
