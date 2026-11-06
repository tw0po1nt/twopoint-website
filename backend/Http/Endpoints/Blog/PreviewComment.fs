namespace TwoPoint.Http.Endpoints.Blog

open TwoPoint.Core.Posts.Actions
open TwoPoint.Http
open TwoPoint.Http.Endpoints

open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging

open System.Net
open System.Threading

type CommentPreviewJson =
  { Content : string option }

type PreviewComment (
  config: Config,
  logger: ILogger<PreviewComment>
) =

  [<Function("Blog-Comments-Preview")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "blog/comments/preview")>] req : HttpRequestData,
    ct : CancellationToken
  ) = task {
    let response = req.CreateResponse HttpStatusCode.OK
    logger.LogInformation "Processing 'Blog.Comments.Preview' request"

    let! json = req.ReadFromJsonAsync<CommentPreviewJson> ct
    let content = json |> Option.ofObj |> Option.bind _.Content |> Option.defaultValue ""

    let apiResponse, statusCode =
      match PreviewComment.preview config.Stage { PreviewComment.Dto.Content = content } with
      | Ok html -> { Success = true; Message = None; Data = Some html }, HttpStatusCode.OK
      | Error errors -> { Success = false; Message = Some (String.concat ", " errors); Data = None }, HttpStatusCode.BadRequest

    response.StatusCode <- statusCode
    do! response.WriteAsJsonAsync(apiResponse, ct)
    return response
  }
