namespace TwoPoint.Http.Endpoints.Admin

open TwoPoint.Core.Posts
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Posts
open TwoPoint.Http
open TwoPoint.Http.Endpoints

open Azure.Storage.Blobs
open IcedTasks
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging

open System.Net
open System.Threading

type SeriesDto = { Slug : string; Title : string }

module GetSeriesQuery = TwoPoint.Core.Posts.Queries.GetSeries

type GetSeries (
  logger : ILogger<GetSeries>,
  postStoreClient: BlobServiceClient
) =

  [<Function("Admin-Series-GetAll")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "internal/series")>] req : HttpRequestData,
    context : FunctionContext,
    ct : CancellationToken
  ) =
    let op = "Admin.Series.GetAll"
    let httpContext = context.GetHttpContext() |> Option.ofObj
    let claimsPrincipal = httpContext |> Option.map _.User

    ct |> (
      Auth.runIfAuthorized logger req claimsPrincipal op
      <| fun _ -> cancellableTask {
        let! ct = CancellableTask.getCancellationToken()
        logger.LogInformation("Processing '{op}' request", op)
        let response = req.CreateResponse HttpStatusCode.OK
        let postStore = PostStore(postStoreClient, logger)

        let getSeries = GetSeriesQuery.withDependencies {
          new GetSeriesQuery.Dependencies with

          member _.GetSeriesRecords () = postStore.GetSeriesRecords()
        }

        let! result = getSeries ()

        let apiResponse, statusCode =
          result
          |> QueryResult.toApiResponse (List.map (fun (series : SeriesListing) -> { SeriesDto.Slug = Slug.value series.Slug; Title = series.Title }))

        response.StatusCode <- statusCode
        do! response.WriteAsJsonAsync(apiResponse, ct)
        return response
      }
    )
