namespace TwoPoint.Http.Endpoints.Admin

open TwoPoint.Core.Subscriptions
open TwoPoint.Core.Util
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

type SubscriptionDto =
  { Id : string
    Email : string
    Name : string
    CreatedDate : DateTime
    /// `active`, `pending`, or `canceled`
    Status : string
    CanceledDate : Nullable<DateTime> }

[<AutoOpen>]
module SubscriptionExt =

  type Subscription with
    member this.ToDto() =
      { SubscriptionDto.Id = this.Id.ToString()
        Email = this.EmailAddress.ToString()
        Name = this.Name |> Option.map _.ToString() |> Option.toObj
        CreatedDate = this.CreatedDate
        Status =
          match this.Status with
          | Active -> "active"
          | Pending _ -> "pending"
          | Canceled _ -> "canceled"
        CanceledDate =
          match this.Status with
          | Active
          | Pending _ -> Nullable()
          | Canceled date -> Nullable date }

module GetAllSubscriptionsQuery = TwoPoint.Core.Subscriptions.Queries.GetAllSubscriptions

type GetAllSubscriptions (
  logger : ILogger<GetAllSubscriptions>,
  tableServiceClient: TableServiceClient
) =

  [<Function("Admin-Subscriptions-GetAll")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "internal/subscriptions")>] req : HttpRequestData,
    context : FunctionContext,
    ct : CancellationToken
  ) =
    let op = "Admin.Subscriptions.GetAll"
    let httpContext = context.GetHttpContext() |> Option.ofObj
    let claimsPrincipal = httpContext |> Option.map _.User

    ct |> (
      Auth.runIfAuthorized logger req claimsPrincipal op
      <| fun _ -> cancellableTask {
        let! ct = CancellableTask.getCancellationToken()
        logger.LogInformation("Processing '{op}' request", op)
        let response = req.CreateResponse HttpStatusCode.OK

        // Dependencies
        let subscriptionDependencies = SubscriptionDependencies(tableServiceClient, SystemDependencies.live, logger)
        let getAllSubscriptions = GetAllSubscriptionsQuery.withDependencies {
          new GetAllSubscriptionsQuery.Dependencies with

          member _.GetAllSubscriptions () = subscriptionDependencies.GetAllSubscriptions()
        }

        let! subscriptionsResult = getAllSubscriptions()
        let apiResponse, statusCode = subscriptionsResult |> QueryResult.toApiResponse (List.map _.ToDto())

        response.StatusCode <- statusCode
        do! response.WriteAsJsonAsync(apiResponse, ct)
        return response
      }
    )
