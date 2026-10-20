module TwoPoint.Core.Tests.Fixtures

open TwoPoint.Core.Posts
open TwoPoint.Core.Shared
open TwoPoint.Core.Subscriptions

open System

let ok result =
  match result with
  | Ok value -> value
  | Error error -> failwithf "Expected Ok, got Error %A" error

let subscriptionId = SubscriptionId.create "2f1c4ed9-6a5b-4f0e-9a43-3d2b8c1e7a10" |> ok

let subscription isActive =
  { Id = subscriptionId
    CreatedDate = DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    EmailAddress = EmailAddress.create "reader@example.com" |> ok
    Name = NonEmptyString.create None "Reader" |> ok |> Some
    IsActive = isActive }

let post slug (createdDate : string) =
  Post.create slug "A post" createdDate PostCommentStats.Zero |> ok

let olderPost = post "older-post" "2026-01-01T00:00:00.0000000Z"
let latestPost = post "latest-post" "2026-02-01T00:00:00.0000000Z"

let delivery deliveryType (post : Post) =
  { Id = SubscriptionDeliveryId.create (Guid.NewGuid().ToString()) |> ok
    CreatedDate = DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc)
    Type = deliveryType
    Post = post.Id }

let slottedHtml =
  $"<html><body><table><tr><td>{Newsletter.IntroSlot}</td></tr></table>"
  + $"<a href=\"{Newsletter.UnsubscribeUrlSlot}\">Unsubscribe</a></body></html>"

let panelHtml =
  $"<html><body><table>{Newsletter.IntroPanelStart}<tbody><tr><td>~/twopoint</td></tr>"
  + $"<tr><td>{Newsletter.IntroSlot}</td></tr></tbody>{Newsletter.IntroPanelEnd}<tr><td><h1>A post</h1></td></tr></table>"
  + $"<a href=\"{Newsletter.UnsubscribeUrlSlot}\">Unsubscribe</a></body></html>"
