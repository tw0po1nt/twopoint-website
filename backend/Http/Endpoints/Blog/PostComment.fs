namespace TwoPoint.Http.Endpoints.Blog

open TwoPoint.Http
open TwoPoint.Http.Endpoints
open TwoPoint.Core.Posts.Actions
open TwoPoint.Core.Posts.Logic
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Posts

open Azure.Communication.Email
open Azure.Data.Tables
open FirebaseAdmin.Messaging
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging

open System.Net
open System.Threading

type NewCommentJson =
  { ValidationId : string option
    Comment : string option }

type PostComment (
  config: Config,
  emailClient: EmailClient,
  messaging : FirebaseMessaging,
  logger: ILogger<PostComment>,
  tableServiceClient: TableServiceClient
) =

  [<Function("Blog-Posts-PostComment")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "blog/posts/{slug}/comments")>] req : HttpRequestData,
    slug : string,
    ct : CancellationToken
  ) = task {
    let response = req.CreateResponse HttpStatusCode.OK
    logger.LogInformation("Processing 'Blog.Posts.PostComment' request with slug '{slug}'", slug)
    let validRedirectUris = config.ValidRedirectUris |> List.map _.Uri

    let! json = req.ReadFromJsonAsync<NewCommentJson> ct

    // Dependencies
    let postDependencies = PostDependencies(
      validRedirectUris,
      emailClient,
      config.Azure.EmailSender,
      messaging,
      tableServiceClient,
      logger
    )
    let postComment = PostComment.withDependencies {
      new PostComment.Dependencies with
      
      member this.GetPostBySlug slug = postDependencies.GetPostBySlug slug
      member this.GetCommenterByValidationId validationId = postDependencies.GetCommenterByValidationId validationId
      member this.CreateComment post commenter newComment = postDependencies.CreateComment post commenter newComment
    }

    let newComment =
      { NewComment.Dto.Post = NewComment.Post slug
        NewComment.Dto.ValidationId = json.ValidationId |> Option.defaultValue "" |> NewComment.ValidationId
        NewComment.Dto.Comment = json.Comment |> Option.map NewComment.Comment |> Option.defaultValue (NewComment.Comment "") }

    let! postCommentResult = ct |> postComment newComment
    let apiResponse, statusCode =
      match postCommentResult with
      | Ok _ | Error (Logic (CommenterBanned _)) ->
        { Success = true; Message = None; Data = None }, HttpStatusCode.OK
      | Error (Logic postCommentError) ->
        let errorMessage =
          match postCommentError with
          | PostNotFound slug -> $"Post '{slug.ToString()}' not found"
          | PostCommentError.CommenterNotFound validationId -> $"Commenter '{validationId.ToString()}' not found"
          | _ -> "Not found"
        { Success = false; Message = Some errorMessage; Data = None }, HttpStatusCode.NotFound
      | Error actionError when actionError.IsValidation ->
        { Success = false;  Message = Some (actionError.ToString()); Data = None }, HttpStatusCode.BadRequest
      | Error actionError ->
        { Success = false; Message = Some (actionError.ToString()); Data = None }, HttpStatusCode.InternalServerError

    response.StatusCode <- statusCode
    do! response.WriteAsJsonAsync(apiResponse, ct)
    return response
  }
