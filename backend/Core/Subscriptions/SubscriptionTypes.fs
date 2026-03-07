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

type Subscription =
  { Id : SubscriptionId
    CreatedDate : DateTime
    EmailAddress : EmailAddress
    Name : NonEmptyString option
    IsActive : bool }
