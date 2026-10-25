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

open System
open System.Net
open System.Threading

/// A missing author, category, series, or image is null.
[<Struct>]
type PostDetailsDto =
  { Author : string
    Category : string
    Tags : string[]
    Series : string
    Image : string
    ShowOnHomepage : bool }

module PostDetailsDto =

  let ofDetails (details : TwoPoint.Site.Render.PostText.Details) =
    { Author = Option.toObj details.Author
      Category = Option.toObj details.Category
      Tags = Array.ofList details.Tags
      Series = Option.toObj details.Series
      Image = Option.toObj details.Image
      ShowOnHomepage = details.ShowOnHomepage }

  let toDetails (dto : PostDetailsDto) : TwoPoint.Site.Render.PostText.Details =
    { Author = Option.ofObj dto.Author
      Category = Option.ofObj dto.Category
      Tags = dto.Tags |> Option.ofObj |> Option.defaultValue [||] |> List.ofArray
      Series = Option.ofObj dto.Series
      Image = Option.ofObj dto.Image
      ShowOnHomepage = dto.ShowOnHomepage }

/// A draft has no published text, so its `Published` is null.
type PublishedDto =
  { Title : string
    Excerpt : string
    Body : string
    Details : PostDetailsDto }

type PostDocumentDto =
  { Slug : string
    State : string
    PendingEdit : bool
    PublishDate : Nullable<DateTime>
    ETag : string
    Title : string
    Excerpt : string
    Body : string
    Details : PostDetailsDto
    Text : string
    Published : PublishedDto }

[<AutoOpen>]
module PostDocumentExt =

  type PostDocument with
    member this.ToDto() =
      let fields = TwoPoint.Site.Render.PostText.split this.Text
      let published =
        match this.PublishedText with
        | Some text ->
          let fields = TwoPoint.Site.Render.PostText.split text

          { Title = fields.Title
            Excerpt = fields.Excerpt
            Body = fields.Body
            Details = PostDetailsDto.ofDetails (TwoPoint.Site.Render.PostText.details text) }
        | None -> Unchecked.defaultof<PublishedDto>


      { PostDocumentDto.Slug = this.Slug.ToString()
        State = this.State.Name
        PendingEdit = this.HasPendingEdit
        PublishDate = Option.toNullable this.PublishDate
        ETag = Option.toObj this.ETag
        Title = fields.Title
        Excerpt = fields.Excerpt
        Body = fields.Body
        Details = PostDetailsDto.ofDetails (TwoPoint.Site.Render.PostText.details this.Text)
        Text = this.Text
        Published = published }

module GetPostDocumentQuery = TwoPoint.Core.Posts.Queries.GetPostDocument

type GetPost (
  logger : ILogger<GetPost>,
  postStoreClient: BlobServiceClient,
  texts : BlobTextCache
) =

  [<Function("Admin-Posts-Get")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "internal/posts/{slug}")>] req : HttpRequestData,
    context : FunctionContext,
    slug : string,
    ct : CancellationToken
  ) =
    let op = "Admin.Posts.Get"
    let httpContext = context.GetHttpContext() |> Option.ofObj
    let claimsPrincipal = httpContext |> Option.map _.User

    ct |> (
      Auth.runIfAuthorized logger req claimsPrincipal op
      <| fun _ -> cancellableTask {
        let! ct = CancellableTask.getCancellationToken()
        logger.LogInformation("Processing '{op}' request with slug '{slug}'", op, slug)
        let response = req.CreateResponse HttpStatusCode.OK
        let postStore = PostStore(postStoreClient, logger, texts)
        let getPost = GetPostDocumentQuery.withDependencies {
          new GetPostDocumentQuery.Dependencies with

          member _.GetUtcNow () = DateTime.UtcNow
          member _.GetPublishedBlob slug = postStore.GetPublishedBlob slug
          member _.GetWorkingBlob slug = postStore.GetWorkingBlob slug
        }

        let! postResult = getPost slug

        let toApiResponse = QueryResult.toApiResponse (fun (document : PostDocument) -> document.ToDto())

        let apiResponse, statusCode =
          match postResult with
          | Ok None -> { Success = false; Message = Some $"Post '{slug}' not found"; Data = None }, HttpStatusCode.NotFound
          | Ok(Some document) -> toApiResponse (Ok document)
          | Error queryError -> toApiResponse (Error queryError)

        response.StatusCode <- statusCode
        do! response.WriteAsJsonAsync(apiResponse, ct)
        return response
      }
    )
