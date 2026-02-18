namespace TwoPoint.Core.Subscriptions

open TwoPoint.Core.Shared

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


type Subscription =
  { Id : SubscriptionId
    CreatedDate : DateTime
    EmailAddress : EmailAddress
    Name : NonEmptyString option
    IsActive : bool }
