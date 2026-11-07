namespace TwoPoint.Http.Endpoints.Blog

open TwoPoint.Core.Posts
open TwoPoint.Core.Posts.Queries
open TwoPoint.Dependencies.Posts
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

type CommentDto =
  { Id : string
    CreatedDate : DateTime
    Commenter : string
    Content : string }

[<AutoOpen>]
module PostTypesExt =

  type RenderedComment with
    member this.ToDto() =
      { CommentDto.Id = this.Comment.Id.ToString()
        CreatedDate = this.Comment.CreatedDate
        Commenter =
          match this.Comment.Writer with
          | CommentWriter.Commenter commenter ->
            commenter.Name |> Option.map _.ToString() |> Option.defaultValue "Anonymous"
          | CommentWriter.Author -> ""
        Content = this.Html }


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
      logger
    )

    let getCommentsForPost = GetCommentsForPost.withDependencies {
      new GetCommentsForPost.Dependencies with

      member _.GetPostBySlug slug = postDependencies.GetPostBySlug slug
      member _.GetCommentsForPost approvals post = postDependencies.GetCommentsForPost approvals post
      member _.Stage = config.Stage
    }

    let onlyApproved = [CommentApproval.Approved.ToString()]
    let! commentsResult = (slug, ct) ||> getCommentsForPost onlyApproved

    let apiResponse, statusCode =
      match commentsResult with
      | Ok (Some comments) ->
        { Success = true; Message = None; Data = comments |> List.map _.ToDto() |> Some }, HttpStatusCode.OK
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
