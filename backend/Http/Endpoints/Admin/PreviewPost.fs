namespace TwoPoint.Http.Endpoints.Admin

open TwoPoint.Core.Posts
open TwoPoint.Core.Posts.Actions
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Posts
open TwoPoint.Http
open TwoPoint.Http.Endpoints

open Azure.Storage.Blobs
open IcedTasks
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging

open System
open System.Net
open System.Threading

type PreviewPostJson =
  { Title : string
    Excerpt : string
    Body : string
    /// Null leaves the details of the stored text as they are.
    Details : Nullable<PostDetailsDto> }

[<AllowNullLiteral>]
type PreviewArticleDto(content : string, outline : string) =
  member _.Content = content
  member _.Outline = outline

/// `Article` is null when the text has an error in its parse.
type PreviewDto =
  { Article : PreviewArticleDto
    Problems : ProblemDto list }

module PreviewPostMapping =

  open TwoPoint.Site.Render

  let preview (checkedPost : Check.CheckedPost) : PreviewDto =
    { Article =
        checkedPost.Article
        |> Option.map (fun article -> PreviewArticleDto(article.Content, article.Outline))
        |> Option.toObj
      Problems = checkedPost.Problems |> List.map ProblemMapping.problem }

type PreviewPost (
  config : Config,
  logger : ILogger<PreviewPost>,
  postStoreClient : BlobServiceClient,
  images : TwoPoint.Site.Render.IImageSize,
  parseCache : PublishedParseCache,
  texts : BlobTextCache
) =

  [<Function("Admin-Posts-Preview")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "internal/posts/{slug}/preview")>] req : HttpRequestData,
    context : FunctionContext,
    slug : string,
    ct : CancellationToken
  ) =
    let op = "Admin.Posts.Preview"
    let httpContext = context.GetHttpContext() |> Option.ofObj
    let claimsPrincipal = httpContext |> Option.map _.User

    ct |> (
      Auth.runIfAuthorized logger req claimsPrincipal op
      <| fun _ -> cancellableTask {
        let! ct = CancellableTask.getCancellationToken()
        logger.LogInformation("Processing '{op}' request with slug '{slug}'", op, slug)
        let response = req.CreateResponse HttpStatusCode.OK

        let! json = req.ReadFromJsonAsync<PreviewPostJson> ct
        let text (value : string) = value |> Option.ofObj |> Option.defaultValue ""
        let json = json |> Option.ofObj |> Option.defaultValue { Title = ""; Excerpt = ""; Body = ""; Details = Nullable() }

        let postStore = PostStore(postStoreClient, logger, texts)
        let previewPost = PreviewPost.withDependencies {
          new PreviewPost.Dependencies with

          member _.Stage = config.Stage
          member _.Images = images
          member _.GetUtcNow () = System.DateTime.UtcNow
          member _.GetWorkingBlob slug = postStore.GetWorkingBlob slug
          member _.GetPublishedPosts () = postStore.GetPublishedPosts()
          member _.GetSeriesRecords () = postStore.GetSeriesRecords()
          member _.ParsePublished = parseCache.Parse
        }

        let! result =
          previewPost
            { Slug = slug
              Title = text json.Title
              Excerpt = text json.Excerpt
              Body = text json.Body
              Details = json.Details |> Option.ofNullable |> Option.map PostDetailsDto.toDetails }

        let apiResponse, statusCode =
          result
          |> ActionResult.toApiResponse
            (fun checkedPost -> Some(PreviewPostMapping.preview checkedPost))
            (function
              | PreviewPost.Error.PostNotFound slug -> $"Post '{slug}' not found", HttpStatusCode.NotFound
              | PreviewPost.Error.FrontMatterUnreadable reason ->
                $"Couldn't read the saved post's front matter: {reason}", HttpStatusCode.UnprocessableEntity)

        response.StatusCode <- statusCode
        do! response.WriteAsJsonAsync(apiResponse, ct)
        return response
      }
    )
