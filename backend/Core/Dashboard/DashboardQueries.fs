namespace TwoPoint.Core.Dashboard.Queries

open TwoPoint.Core.Posts
open TwoPoint.Core.Posts.Queries
open TwoPoint.Core.Subscriptions
open TwoPoint.Core.Subscriptions.Queries
open TwoPoint.Core.Util

open FsToolkit.ErrorHandling
open IcedTasks

open System

// ====================================================================================================================
// Get the dashboard
// ====================================================================================================================

[<RequireQualifiedAccess>]
module GetDashboard =

  type Counts =
    { Drafts : int
      PendingEdits : int
      DeliveriesToStart : int
      ActiveSubscriptions : int
      NewSubscriptions : int
      CanceledSubscriptions : int }

  type Query = unit -> CancellableTask<QueryResult<Counts>>

  let audienceWindow = TimeSpan.FromDays 30.0

  [<RequireQualifiedAccess>]
  module Logic =

    let counts (utcNow : DateTime) (posts : PostListing list) (subscriptions : Subscription list) : Counts =
      let since = utcNow - audienceWindow
      let count predicate items = items |> List.filter predicate |> List.length

      { Drafts = posts |> count (fun post -> post.State = Draft)
        PendingEdits = posts |> count _.HasPendingEdit
        DeliveriesToStart = posts |> count _.DeliveryToStart
        ActiveSubscriptions = subscriptions |> count Subscription.isActive
        NewSubscriptions =
          subscriptions
          |> count (fun subscription -> not (Subscription.isPending subscription) && subscription.CreatedDate >= since)
        CanceledSubscriptions =
          subscriptions
          |> count (fun subscription ->
            match subscription.Status with
            | Canceled date -> date >= since
            | Active
            | Pending _ -> false) }

  let withDependencies (posts : GetAllPosts.Dependencies) (subscriptions : GetAllSubscriptions.Dependencies) : Query =
    fun () -> cancellableTaskResult {
      let! postList, subscriptionList =
        CancellableTaskResult.parallelZip
          (GetAllPosts.withDependencies posts ())
          (GetAllSubscriptions.withDependencies subscriptions ())

      return Logic.counts (posts.GetUtcNow()) postList subscriptionList
    }
