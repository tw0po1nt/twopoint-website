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

type TermDto = { Slug : string; Title : string; Value : string; Posts : int }

type TermSuggestionsDto = { Categories : TermDto list; Tags : TermDto list }

type SeriesDto = { Slug : string; Title : string }

module GetTermsQuery = TwoPoint.Core.Posts.Queries.GetTerms
module GetSeriesQuery = TwoPoint.Core.Posts.Queries.GetSeries

type GetTerms (
  logger : ILogger<GetTerms>,
  postStoreClient: BlobServiceClient
) =

  [<Function("Admin-Terms-GetAll")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "internal/terms")>] req : HttpRequestData,
    context : FunctionContext,
    ct : CancellationToken
  ) =
    let op = "Admin.Terms.GetAll"
    let httpContext = context.GetHttpContext() |> Option.ofObj
    let claimsPrincipal = httpContext |> Option.map _.User

    ct |> (
      Auth.runIfAuthorized logger req claimsPrincipal op
      <| fun _ -> cancellableTask {
        let! ct = CancellableTask.getCancellationToken()
        logger.LogInformation("Processing '{op}' request", op)
        let response = req.CreateResponse HttpStatusCode.OK
        let postStore = PostStore(postStoreClient, logger)

        let getTerms = GetTermsQuery.withDependencies {
          new GetTermsQuery.Dependencies with

          member _.GetPublishedPosts () = postStore.GetPublishedPosts()
        }

        let toDto (term : TermCount) : TermDto = { Slug = term.Slug; Title = term.Title; Value = term.Value; Posts = term.Posts }

        let! result = getTerms ()

        let apiResponse, statusCode =
          result
          |> QueryResult.toApiResponse (fun suggestions ->
            { Categories = suggestions.Categories |> List.map toDto
              Tags = suggestions.Tags |> List.map toDto })

        response.StatusCode <- statusCode
        do! response.WriteAsJsonAsync(apiResponse, ct)
        return response
      }
    )

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
          |> QueryResult.toApiResponse (List.map (fun (series : SeriesListing) -> { SeriesDto.Slug = series.Slug; Title = series.Title }))

        response.StatusCode <- statusCode
        do! response.WriteAsJsonAsync(apiResponse, ct)
        return response
      }
    )
