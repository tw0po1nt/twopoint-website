namespace TwoPoint.Http.Endpoints.Admin

open TwoPoint.Core.Deploys
open TwoPoint.Core.Posts
open TwoPoint.Core.Posts.Actions
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Posts
open TwoPoint.Http
open TwoPoint.Http.Endpoints

open Azure.Core
open Azure.Data.Tables
open Azure.Storage.Blobs
open IcedTasks
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging

open System
open System.Net
open System.Net.Http
open System.Threading

/// A null `Slug` keeps the slug of the route.
type SaveSeriesJson =
  { Slug : string
    Title : string
    Excerpt : string
    Image : string }

/// Each warning is one of the rebuild warnings of `PublishedPostDto`, or `old-record`.
type SavedSeriesDto =
  { Slug : string
    ETag : string
    ProductionRun : Nullable<int64>
    Warnings : string list }

module SaveSeriesMapping =

  let problem (problem : SaveSeries.FormProblem) =
    match problem with
    | SaveSeries.EmptyTitle -> "Give the series a title."
    | SaveSeries.EmptyExcerpt -> "Give the series an excerpt."
    | SaveSeries.BadSlug slug -> $"The slug '{slug}' must have lowercase letters and digits, with a single - between them."
    | SaveSeries.BadImage image -> $"The image '{image}' must be an https URL."

  let warning (warning : SaveSeries.Warning) =
    match warning with
    | SaveSeries.Rebuild step -> PublishMapping.warning step
    | SaveSeries.OldRecordKept
    | SaveSeries.OldRecordNotDeleted _ -> "old-record"

  let saved (saved : SaveSeries.Saved) : SavedSeriesDto =
    { Slug = Slug.value saved.Slug
      ETag = saved.ETag
      ProductionRun = ProductionDispatch.runNumber saved.Production |> Option.map _.Value |> Option.toNullable
      Warnings = saved.Warnings |> List.map warning }

  let error (error : SaveSeries.Error) =
    match error with
    | SaveSeries.Invalid problems -> problems |> List.map problem |> String.concat " ", HttpStatusCode.UnprocessableEntity
    | SaveSeries.SeriesNotFound slug -> $"Series '{slug}' not found", HttpStatusCode.NotFound
    | SaveSeries.SeriesChanged -> "The series changed since you opened it", HttpStatusCode.PreconditionFailed
    | SaveSeries.SlugTaken slug -> $"Another series has the slug '{slug}'.", HttpStatusCode.Conflict
    | SaveSeries.SlugLocked posts ->
      $"The slug stays while a post refers to the series: {SeriesMapping.titles posts}.", HttpStatusCode.Conflict

type SaveSeries(
  config : Config,
  credential : TokenCredential,
  environment : IHostEnvironment,
  httpClientFactory : IHttpClientFactory,
  logger : ILogger<SaveSeries>,
  postStoreClient : BlobServiceClient,
  tableServiceClient : TableServiceClient,
  texts : BlobTextCache
) =

  [<Function("Admin-Series-Save")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "internal/series/{slug}")>] req : HttpRequestData,
    context : FunctionContext,
    slug : string,
    ct : CancellationToken
  ) =
    let op = "Admin.Series.Save"
    let httpContext = context.GetHttpContext() |> Option.ofObj
    let claimsPrincipal = httpContext |> Option.map _.User

    ct |> (
      Auth.runIfAuthorized logger req claimsPrincipal op
      <| fun _ -> cancellableTask {
        let! ct = CancellableTask.getCancellationToken()
        logger.LogInformation("Processing '{op}' request with slug '{slug}'", op, slug)
        let response = req.CreateResponse HttpStatusCode.OK

        match SaveDraftHeaders.precondition req with
        | Missing ->
          return!
            PreconditionResponse.missingWith<SavedSeriesDto> "Send If-Match with the ETag, or If-None-Match: * for a new series" req
        | precondition ->
          let! json = req.ReadFromJsonAsync<SaveSeriesJson> ct
          let json = json |> Option.ofObj |> Option.defaultValue { Slug = null; Title = ""; Excerpt = ""; Image = null }
          let text (value : string) = value |> Option.ofObj |> Option.defaultValue ""

          let postStore = PostStore(postStoreClient, logger, texts)
          let rebuilder = SiteRebuilder(config, credential, environment, httpClientFactory, logger, tableServiceClient)

          let saveSeries = SaveSeries.withDependencies {
            new SaveSeries.Dependencies with

            member _.GetUtcNow () = DateTime.UtcNow
            member _.GetSeriesRecords () = postStore.GetSeriesRecords()
            member _.GetPublishedPosts () = postStore.GetPublishedPosts()
            member _.GetWorkingPosts () = postStore.GetWorkingPosts()
            member _.WriteSeriesBlob slug text condition = postStore.WriteSeriesBlob slug text condition
            member _.DeleteSeriesBlob slug eTag = postStore.DeleteSeriesBlob slug eTag
            member _.DispatchSite () = rebuilder.Dispatch()
          }

          let! result =
            saveSeries
              { Slug = slug
                IfMatch = SaveDraftHeaders.ifMatch precondition
                NewSlug = json.Slug |> Option.ofObj |> Option.filter (fun typed -> typed <> slug)
                Title = text json.Title
                Excerpt = text json.Excerpt
                Image = json.Image |> Option.ofObj }

          match result with
          | Ok(SaveSeries.SeriesSaved saved) ->
            for warning in saved.Warnings do
              logger.LogWarning("The series '{slug}' is saved, and a step after the save failed: {warning}", saved.Slug, warning)
          | Error _ -> ()

          let apiResponse, statusCode =
            result
            |> ActionResult.toApiResponse (fun (SaveSeries.SeriesSaved saved) -> Some(SaveSeriesMapping.saved saved)) SaveSeriesMapping.error

          response.StatusCode <- statusCode
          do! response.WriteAsJsonAsync(apiResponse, ct)
          return response
      }
    )

type DeleteSeries(
  logger : ILogger<DeleteSeries>,
  postStoreClient : BlobServiceClient,
  texts : BlobTextCache
) =

  [<Function("Admin-Series-Delete")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "internal/series/{slug}")>] req : HttpRequestData,
    context : FunctionContext,
    slug : string,
    ct : CancellationToken
  ) =
    let op = "Admin.Series.Delete"
    let httpContext = context.GetHttpContext() |> Option.ofObj
    let claimsPrincipal = httpContext |> Option.map _.User

    ct |> (
      Auth.runIfAuthorized logger req claimsPrincipal op
      <| fun _ -> cancellableTask {
        let! ct = CancellableTask.getCancellationToken()
        logger.LogInformation("Processing '{op}' request with slug '{slug}'", op, slug)
        let response = req.CreateResponse HttpStatusCode.OK

        match SaveDraftHeaders.precondition req |> SaveDraftHeaders.ifMatch with
        | None -> return! PreconditionResponse.missing<unit> req
        | Some eTag ->
          let postStore = PostStore(postStoreClient, logger, texts)

          let deleteSeries = DeleteSeries.withDependencies {
            new DeleteSeries.Dependencies with

            member _.GetUtcNow () = DateTime.UtcNow
            member _.GetSeriesRecords () = postStore.GetSeriesRecords()
            member _.GetPublishedPosts () = postStore.GetPublishedPosts()
            member _.GetWorkingPosts () = postStore.GetWorkingPosts()
            member _.DeleteSeriesBlob slug eTag = postStore.DeleteSeriesBlob slug eTag
          }

          let! result = deleteSeries { Slug = slug; IfMatch = eTag }

          let apiResponse, statusCode =
            result
            |> ActionResult.toApiResponse
              (fun (DeleteSeries.SeriesDeleted _) -> (None : unit option))
              (fun error ->
                match error with
                | DeleteSeries.SeriesNotFound missing -> $"Series '{missing}' not found", HttpStatusCode.NotFound
                | DeleteSeries.SeriesChanged -> "The series changed since you opened it", HttpStatusCode.PreconditionFailed
                | DeleteSeries.SeriesInUse posts ->
                  $"Posts refer to the series, so it stays: {SeriesMapping.titles posts}.", HttpStatusCode.Conflict)

          response.StatusCode <- statusCode
          do! response.WriteAsJsonAsync(apiResponse, ct)
          return response
      }
    )
