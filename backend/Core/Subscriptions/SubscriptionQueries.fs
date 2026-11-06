namespace TwoPoint.Core.Subscriptions.Queries

open TwoPoint.Core.Posts
open TwoPoint.Core.Subscriptions
open TwoPoint.Core.Util

open FsToolkit.ErrorHandling
open IcedTasks

// ====================================================================================================================
// Get all subscriptions
// ====================================================================================================================

[<RequireQualifiedAccess>]
module GetAllSubscriptions =

  type Query = unit -> CancellableTask<QueryResult<Subscription list>>

  type Dependencies =
    abstract member GetAllSubscriptions: unit -> DependencyResult<Subscription list>

  let withDependencies (dependencies : Dependencies) : Query =
    fun () ->
      dependencies.GetAllSubscriptions()
      |> CancellableTaskResult.mapError QueryError.Dependency
      |> CancellableTaskResult.map (List.sortByDescending _.CreatedDate)

// ====================================================================================================================
// Get deliveries of a subscription
// ====================================================================================================================

[<RequireQualifiedAccess>]
module GetSubscriptionDeliveries =

  type Query = string -> CancellableTask<QueryResult<DeliveredPost list option>>

  type Dependencies =
    abstract member GetSubscriptionById: SubscriptionId -> DependencyResult<Subscription option>
    abstract member GetDeliveriesForSubscription: Subscription -> DependencyResult<SubscriptionDelivery list>
    abstract member GetPostsBySlugs: Slug list -> DependencyResult<Post list>

  let withDependencies (dependencies : Dependencies) : Query =
    fun subscriptionId -> cancellableTaskResult {
      let! subscriptionId =
        subscriptionId
        |> SubscriptionId.create
        |> Result.mapError QueryError.Validation
      let! subscription =
        dependencies.GetSubscriptionById subscriptionId
        |> CancellableTaskResult.mapError QueryError.Dependency
      return!
        subscription
        |> Option.traverseCancellableTaskResult (fun subscription -> cancellableTaskResult {
          let! deliveries = dependencies.GetDeliveriesForSubscription subscription
          let! posts = dependencies.GetPostsBySlugs (deliveries |> List.map _.Post |> List.distinct)
          return
            deliveries
            |> List.sortByDescending _.CreatedDate
            |> List.map (fun delivery ->
              { Delivery = delivery
                PostTitle = posts |> List.tryFind (fun (post : Post) -> post.Id = delivery.Post) |> Option.map _.Info.Title })
        })
        |> CancellableTaskResult.mapError QueryError.Dependency
    }
