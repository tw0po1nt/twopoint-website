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

/// `State` is `published`, `upcoming`, `draft`, or `pending-edit`. `PublishDate` is null for a draft and a pending edit.
type SeriesPostDto =
  { Slug : string
    Title : string
    State : string
    PublishDate : string }

/// `Posts` has the live posts in publish date order, then the drafts and the pending edits.
type SeriesDto =
  { Slug : string
    Title : string
    Excerpt : string
    Image : string
    ETag : string
    Posts : SeriesPostDto list }

module SeriesMapping =

  let post (post : SeriesPost) : SeriesPostDto =
    let state, publishDate =
      match post.Reference with
      | LivePost(PostState.Upcoming, date) -> "upcoming", date
      | LivePost(_, date) -> "published", date
      | DraftPost -> "draft", None
      | PendingEditOnly -> "pending-edit", None

    { Slug = Slug.value post.Slug
      Title = post.Title
      State = state
      PublishDate = publishDate |> Option.map (System.DateTimeOffset >> TwoPoint.Site.Render.Time.isoTime) |> Option.toObj }

  let series (series : SeriesListing) : SeriesDto =
    { Slug = Slug.value series.Slug
      Title = series.Title
      Excerpt = series.Excerpt
      Image = series.Image |> Option.map string |> Option.toObj
      ETag = series.ETag
      Posts = series.Posts |> List.map post }

  let titles (posts : SeriesPost list) =
    posts |> List.map (fun post -> $"'{post.Title}'") |> String.concat ", "

module GetSeriesQuery = TwoPoint.Core.Posts.Queries.GetSeries

type GetSeries (
  logger : ILogger<GetSeries>,
  postStoreClient: BlobServiceClient,
  texts : BlobTextCache
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
        let postStore = PostStore(postStoreClient, logger, texts)

        let getSeries = GetSeriesQuery.withDependencies {
          new GetSeriesQuery.Dependencies with

          member _.GetUtcNow () = System.DateTime.UtcNow
          member _.GetSeriesRecords () = postStore.GetSeriesRecords()
          member _.GetPublishedPosts () = postStore.GetPublishedPosts()
          member _.GetWorkingPosts () = postStore.GetWorkingPosts()
        }

        let! result = getSeries ()

        let apiResponse, statusCode =
          result
          |> QueryResult.toApiResponse (List.map SeriesMapping.series)

        response.StatusCode <- statusCode
        do! response.WriteAsJsonAsync(apiResponse, ct)
        return response
      }
    )
