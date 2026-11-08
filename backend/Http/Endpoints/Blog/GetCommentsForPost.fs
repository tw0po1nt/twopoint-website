namespace TwoPoint.Http.Endpoints.Blog

open TwoPoint.Core.Posts
open TwoPoint.Core.Posts.Queries
open TwoPoint.Dependencies.Posts
open TwoPoint.Dependencies.Util
open TwoPoint.Http
open TwoPoint.Http.Endpoints

open Azure.Communication.Email
open Azure.Data.Tables
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging

open System
open System.Net
open System.Threading

type GetCommentsForPost (
  config: Config,
  emailClient: EmailClient,
  logger : ILogger<GetCommentsForPost>,
  tableServiceClient: TableServiceClient
) =

  [<Function("Blog-Posts-GetComments")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "blog/posts/{slug}/comments")>] req : HttpRequestData,
    slug : string,
    ct : CancellationToken
  ) = task {
    let response = req.CreateResponse HttpStatusCode.OK
    logger.LogInformation("Processing 'Blog.Posts.GetComments' request with slug '{slug}'", slug)

    let validRedirectUris = config.ValidRedirectUris |> List.map _.Uri

    // Dependencies
    let postDependencies = PostDependencies(
      validRedirectUris,
      emailClient,
      config.Azure.EmailSender,
      tableServiceClient,
      SystemDependencies.live,
      logger
    )

    let getPublicComments = GetPublicComments.withDependencies {
      new GetCommentsForPost.Dependencies with

      member _.GetPostBySlug slug = postDependencies.GetPostBySlug slug
      member _.GetCommentsForPost approvals post = postDependencies.GetCommentsForPost approvals post
      member _.Stage = config.Stage
    }

    let! commentsResult = ct |> getPublicComments slug

    let apiResponse, statusCode =
      match commentsResult with
      | Ok (Some comments) ->
        { Success = true; Message = None; Data = Some comments }, HttpStatusCode.OK
      | Ok None ->
        { Success = false; Message = Some $"Post ''{slug}'' not found"; Data = None }, HttpStatusCode.NotFound
      | Error queryError when queryError.IsValidation ->
        { Success = false;  Message = Some (queryError.ToString()); Data = None }, HttpStatusCode.BadRequest
      | Error queryError ->
        { Success = false; Message = Some (queryError.ToString()); Data = None }, HttpStatusCode.InternalServerError

    response.StatusCode <- statusCode
    do! response.WriteAsJsonAsync(apiResponse, ct)
    return response
  }
