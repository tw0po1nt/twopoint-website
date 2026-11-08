namespace TwoPoint.Http.Endpoints.Admin

open TwoPoint.Core.Subscriptions
open TwoPoint.Dependencies.Subscriptions
open TwoPoint.Dependencies.Util
open TwoPoint.Http
open TwoPoint.Http.Endpoints

open Azure.Data.Tables
open IcedTasks
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging

open System
open System.Net
open System.Threading

type SubscriptionDeliveryDto =
  { Id : string
    CreatedDate : DateTime
    Type : string
    PostSlug : string
    PostTitle : string }

[<AutoOpen>]
module DeliveredPostExt =

  type DeliveredPost with
    member this.ToDto() =
      let slug = this.Delivery.Post.ToString()
      { SubscriptionDeliveryDto.Id = this.Delivery.Id.ToString()
        CreatedDate = this.Delivery.CreatedDate
        Type =
          match this.Delivery.Type with
          | Welcome -> "welcome"
          | NewPost -> "newpost"
        PostSlug = slug
        PostTitle = this.PostTitle |> Option.map _.ToString() |> Option.defaultValue slug }

module GetSubscriptionDeliveriesQuery = TwoPoint.Core.Subscriptions.Queries.GetSubscriptionDeliveries

type GetSubscriptionDeliveries (
  logger : ILogger<GetSubscriptionDeliveries>,
  tableServiceClient: TableServiceClient
) =

  [<Function("Admin-Subscriptions-GetDeliveries")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "internal/subscriptions/{subscriptionId}/deliveries")>] req : HttpRequestData,
    context : FunctionContext,
    subscriptionId : string,
    ct : CancellationToken
  ) =
    let op = "Admin.Subscriptions.GetDeliveries"
    let httpContext = context.GetHttpContext() |> Option.ofObj
    let claimsPrincipal = httpContext |> Option.map _.User

    ct |> (
      Auth.runIfAuthorized logger req claimsPrincipal op
      <| fun _ -> cancellableTask {
        let! ct = CancellableTask.getCancellationToken()
        logger.LogInformation("Processing '{op}' request with subscription id '{subscriptionId}'", op, subscriptionId)
        let response = req.CreateResponse HttpStatusCode.OK

        // Dependencies
        let subscriptionDependencies = SubscriptionDependencies(tableServiceClient, SystemDependencies.live, logger)
        let getDeliveries = GetSubscriptionDeliveriesQuery.withDependencies {
          new GetSubscriptionDeliveriesQuery.Dependencies with

          member _.GetSubscriptionById id = subscriptionDependencies.GetSubscriptionById id
          member _.GetDeliveriesForSubscription subscription =
            subscriptionDependencies.GetDeliveriesForSubscription subscription
          member _.GetPostsBySlugs slugs = subscriptionDependencies.GetPostsBySlugs slugs
        }

        let! deliveriesResult = getDeliveries subscriptionId

        let apiResponse, statusCode =
          match deliveriesResult with
          | Ok (Some deliveries) ->
            { Success = true; Message = None; Data = Some (deliveries |> List.map _.ToDto()) }, HttpStatusCode.OK
          | Ok None ->
            { Success = false; Message = Some $"Subscription '{subscriptionId}' not found"; Data = None }, HttpStatusCode.NotFound
          | Error queryError when queryError.IsValidation ->
            { Success = false; Message = Some (queryError.ToString()); Data = None }, HttpStatusCode.BadRequest
          | Error queryError ->
            { Success = false; Message = Some (queryError.ToString()); Data = None }, HttpStatusCode.InternalServerError

        response.StatusCode <- statusCode
        do! response.WriteAsJsonAsync(apiResponse, ct)
        return response
      }
    )
