namespace TwoPoint.Http.Endpoints.Admin

open TwoPoint.Core.Posts
open TwoPoint.Core.Posts.Api
open TwoPoint.Core.Posts.Dependencies
open TwoPoint.Http
open TwoPoint.Http.Endpoints

open Azure.Communication.Email
open Azure.Data.Tables
open FirebaseAdmin.Messaging
open IcedTasks
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging

open System
open System.Net
open System.Threading

type AdminCommenterDto =
  { Email : string
    Name : string
    Status : string }

type AdminCommentDto =
  { Id : string
    CreatedDate : DateTime
    Commenter : AdminCommenterDto
    Status : string
    Content : string }
  
[<AutoOpen>]
module CommentTypesExt =
  
  type Commenter with
    member this.ToDto() =
      { AdminCommenterDto.Email = this.EmailAddress.ToString()
        Name = this.Name |> Option.map _.ToString() |> Option.defaultValue "Anonymous"
        Status = this.Status.ToString() }
  
  type Comment with
    member this.ToDto() =
      { AdminCommentDto.Id = this.Id.ToString()
        CreatedDate = this.CreatedDate
        Commenter = this.Commenter.ToDto()
        Status = this.Status.Approval.ToString()
        Content = this.Content.ToString() }

type AdminGetCommentsForPost (
  config: Config,
  emailClient: EmailClient,
  messaging : FirebaseMessaging,
  logger : ILogger<AdminGetCommentsForPost>,
  tableServiceClient: TableServiceClient
) =
  
  [<Function("Admin-Posts-GetComments")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "internal/posts/{slug}/comments")>] req : HttpRequestData,
    context : FunctionContext,
    slug : string,
    ct : CancellationToken
  ) =
    let op = "Admin.Posts.GetComments"
    let httpContext = context.GetHttpContext() |> Option.ofObj
    let claimsPrincipal = httpContext |> Option.map _.User
    ct |> (
      Auth.runIfAuthorized logger req claimsPrincipal op
      <| fun _ -> cancellableTask {
        let response = req.CreateResponse HttpStatusCode.OK
        logger.LogInformation("Processing 'Admin.Posts.GetComments' request with slug '{slug}'", slug)
        
        let validRedirectUris = config.ValidRedirectUris |> List.map _.Uri
        
        // Dependencies
        let postDependencies =
          PostDependencies.live
            validRedirectUris
            emailClient
            config.Azure.EmailSender
            messaging
            tableServiceClient
            logger
        let postQueries = PostQueries.withDependencies postDependencies
        
        let! commentsResult = (slug, ct) ||> postQueries.GetCommentsForPost []
        
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
    )
