module TwoPoint.Core.Tests.Fixtures

open TwoPoint.Core.Deploys
open TwoPoint.Core.Posts
open TwoPoint.Core.Shared
open TwoPoint.Core.Subscriptions

open System

let ok result =
  match result with
  | Ok value -> value
  | Error error -> failwithf "Expected Ok, got Error %A" error

let subscriptionId = SubscriptionId.create "2f1c4ed9-6a5b-4f0e-9a43-3d2b8c1e7a10" |> ok

let canceledStatus = Canceled(DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc))

let subscription (status : SubscriptionStatus) =
  { Id = subscriptionId
    CreatedDate = DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    EmailAddress = EmailAddress.create "reader@example.com" |> ok
    Name = NonEmptyString.create None "Reader" |> ok |> Some
    Status = status }

let post slug (createdDate : string) =
  Post.create slug "A post" createdDate PostCommentStats.Zero |> ok

let olderPost = post "older-post" "2026-01-01T00:00:00.0000000Z"
let latestPost = post "latest-post" "2026-02-01T00:00:00.0000000Z"

let postedOn (post : Post) (comment : Comment) =
  { PostedComment.Post = post.Id; PostTitle = post.Info.Title; Comment = comment }

/// The store reads the replies of a top-level comment only in the partition of its post.
let repliesIn (stored : PostedComment seq) (topLevels : PostedComment list) =
  stored
  |> Seq.filter (fun reply ->
    topLevels |> List.exists (fun topLevel -> reply.Post = topLevel.Post && reply.Comment.Parent = Some topLevel.Comment.Id))
  |> Seq.map _.Comment
  |> List.ofSeq

let noDeploys =
  { Changes = Map.empty
    Rebuild = None
    Runs = { Latest = None; LatestSucceeded = None } }

/// The deploy context of one change to the post, with no run yet.
let changed (post : Post) (production : ProductionDispatch) =
  { noDeploys with Changes = Map.ofList [ post.Id, { ChangedDate = post.Info.CreatedDate; Production = production } ] }

let deploying (post : Post) = changed post (Dispatched (Queued (RunNumber 9L)))

let deployFailed (post : Post) = changed post ProductionDispatch.Failed

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
